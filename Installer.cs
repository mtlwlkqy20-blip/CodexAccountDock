using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Codex Account Dock Setup")]
[assembly: AssemblyProduct("Codex Account Dock")]
[assembly: AssemblyCompany("Codex Account Dock contributors")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]
[assembly: AssemblyInformationalVersion("3.0.0")]

namespace CodexAccountDock.Setup
{
    internal static class Program
    {
        internal const string ProductVersion = "3.0.0";
        internal const string ProductExeName = "CodexAccountDock.exe";
        private const string PayloadResourceName = "CodexAccountDock.Payload.zip";

        private static readonly string[] RequiredPayloadFiles = new string[]
        {
            "CodexAccountDock.exe",
            "LICENSE",
            "README.md",
            "assets/app.ico",
        };

        [STAThread]
        private static int Main(string[] args)
        {
            bool nonInteractive = HasArgument(args, "--package-test") || HasArgument(args,"--default-path-test") ||
                IndexOfArgument(args, "--install-test") >= 0 ||
                IndexOfArgument(args, "--upgrade-test") >= 0;
            try
            {
                string frameworkError;
                if (!FrameworkCheck.IsNet462OrLater(out frameworkError))
                {
                    if (!nonInteractive && Environment.UserInteractive)
                        MessageBox.Show(frameworkError, "Codex Account Dock 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else
                        Console.Error.WriteLine(frameworkError);
                    return 10;
                }

                if(HasArgument(args,"--default-path-test"))
                {
                    string actual=GetDefaultInstallDirectory();
                    string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Programs\CodexAccountDock");
                    if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(actual))throw new InvalidOperationException("目标电脑安装路径检测失败");
                    Console.WriteLine("INSTALL_PATH_OK current_user_localappdata no_developer_path user_browse_supported");
                    return 0;
                }
                if (HasArgument(args, "--package-test"))
                {
                    Payload.ReadAndValidate(PayloadResourceName, RequiredPayloadFiles);
                    return 0;
                }

                int upgradeIndex = IndexOfArgument(args, "--upgrade-test");
                if (upgradeIndex >= 0)
                {
                    if (upgradeIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[upgradeIndex + 1]))
                        throw new InvalidOperationException("--upgrade-test 后必须提供测试安装目录。");

                    string upgradeDirectory = Path.GetFullPath(args[upgradeIndex + 1]);
                    InstallerEngine.ValidateUpgradeTestDirectory(upgradeDirectory);
                    InstallerEngine.Install(upgradeDirectory, false, false);
                    return 0;
                }

                int testIndex = IndexOfArgument(args, "--install-test");
                if (testIndex >= 0)
                {
                    if (testIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[testIndex + 1]))
                        throw new InvalidOperationException("--install-test 后必须提供一个空的测试目录。");

                    string testDirectory = Path.GetFullPath(args[testIndex + 1]);
                    InstallerEngine.ValidateTestDirectory(testDirectory);
                    InstallerEngine.Install(testDirectory, false, false);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerForm());
                return InstallerForm.ProcessExitCode;
            }
            catch (Exception ex)
            {
                if (!nonInteractive && Environment.UserInteractive)
                    MessageBox.Show(ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static bool HasArgument(string[] args, string value)
        {
            return IndexOfArgument(args, value) >= 0;
        }

        private static int IndexOfArgument(string[] args, string value)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        internal static string GetDefaultInstallDirectory()
        {
            // Resolve on the destination computer, under the installing user.
            // Never ship a development-machine-specific drive or account path.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\CodexAccountDock");
        }

        internal static string[] GetRequiredPayloadFiles()
        {
            return (string[])RequiredPayloadFiles.Clone();
        }

        internal static string GetPayloadResourceName()
        {
            return PayloadResourceName;
        }
    }

    internal static class FrameworkCheck
    {
        internal static bool IsNet462OrLater(out string error)
        {
            error = null;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                error = "Codex Account Dock 仅支持 Windows。";
                return false;
            }

            try
            {
                using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                    .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    object releaseValue = key == null ? null : key.GetValue("Release");
                    int release = releaseValue == null ? 0 : Convert.ToInt32(releaseValue);
                    if (release >= 394802) return true;
                }
            }
            catch (Exception ex)
            {
                error = "无法检查 .NET Framework：" + ex.Message;
                return false;
            }

            error = "需要 .NET Framework 4.6.2 或更高版本。请先通过 Windows Update 安装后再运行本安装程序。";
            return false;
        }
    }

    internal sealed class PayloadFile
    {
        internal string RelativePath;
        internal byte[] Content;
    }

    internal static class Payload
    {
        internal static IList<PayloadFile> ReadAndValidate(string resourceName, IEnumerable<string> allowedFiles)
        {
            Dictionary<string, string> allowlist = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in allowedFiles)
            {
                string normalized = NormalizeRelativePath(file);
                allowlist.Add(normalized, normalized);
            }

            Dictionary<string, PayloadFile> result = new Dictionary<string, PayloadFile>(StringComparer.OrdinalIgnoreCase);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream resource = assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null) throw new InvalidDataException("安装包缺少内置文件，可能下载不完整。");
                using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
                {
                    long totalBytes = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string entryName = entry.FullName.Replace('\\', '/');
                        if (entryName.EndsWith("/", StringComparison.Ordinal)) continue;

                        string normalized = NormalizeRelativePath(entryName);
                        if (!allowlist.ContainsKey(normalized))
                            throw new InvalidDataException("安装包包含未允许的文件：" + entry.FullName);
                        if (result.ContainsKey(normalized))
                            throw new InvalidDataException("安装包包含重复文件：" + entry.FullName);
                        if (entry.Length < 0 || entry.Length > (normalized.Equals("extras/BailongSetup.exe",StringComparison.OrdinalIgnoreCase)?1024L:100L) * 1024L * 1024L)
                            throw new InvalidDataException("安装包文件大小异常：" + entry.FullName);

                        totalBytes += entry.Length;
                        if (totalBytes > 1200L * 1024L * 1024L)
                            throw new InvalidDataException("安装包总大小异常。");

                        using (Stream input = entry.Open())
                        using (MemoryStream output = new MemoryStream())
                        {
                            input.CopyTo(output);
                            if (output.Length != entry.Length)
                                throw new InvalidDataException("安装包文件读取不完整：" + entry.FullName);
                            result.Add(normalized, new PayloadFile { RelativePath = normalized, Content = output.ToArray() });
                        }
                    }
                }
            }

            foreach (string required in allowlist.Keys)
            {
                if (!result.ContainsKey(required))
                    throw new InvalidDataException("安装包缺少必要文件：" + required);
            }
            return result.Values.OrderBy(delegate(PayloadFile f) { return f.RelativePath; }, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("安装包中存在空文件名。");
            string normalized = value.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(":"))
                throw new InvalidDataException("安装包中存在绝对路径：" + value);

            string[] parts = normalized.Split('/');
            if (parts.Any(delegate(string part) { return part.Length == 0 || part == "." || part == ".."; }))
                throw new InvalidDataException("安装包中存在不安全路径：" + value);
            return string.Join("/", parts);
        }
    }

    internal static class InstallerEngine
    {
        internal static void ValidateTestDirectory(string directory)
        {
            if (IsDriveRoot(directory)) throw new InvalidOperationException("测试目录不能是磁盘根目录。");
            DirectoryInfo info = Directory.CreateDirectory(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("测试目录不能是链接或联接点。");
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException("--install-test 仅允许写入空目录。");
        }

        internal static void ValidateUpgradeTestDirectory(string directory)
        {
            if (IsDriveRoot(directory) || !Directory.Exists(directory))
                throw new InvalidOperationException("升级测试目录不存在或不安全。");
            DirectoryInfo info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("升级测试目录不能是链接或联接点。");

            const string marker = "synthetic-qa-no-credentials";
            string dataFixture = Path.Combine(directory, @"data\qa-fixture.txt");
            string backupFixture = Path.Combine(directory, @"backups\qa-backup.txt");
            if (!File.Exists(dataFixture) || !File.Exists(backupFixture) ||
                File.ReadAllText(dataFixture) != marker || File.ReadAllText(backupFixture) != marker)
                throw new InvalidOperationException("升级测试缺少合成数据标记，已拒绝执行。");
        }

        internal static void Install(string targetDirectory, bool createDesktopShortcut, bool createStartMenuShortcut)
        {
            string target = ValidateTargetDirectory(targetDirectory);
            EnsureAppIsNotRunning(target);
            EnsureWritable(target);

            IList<PayloadFile> files = Payload.ReadAndValidate(Program.GetPayloadResourceName(), Program.GetRequiredPayloadFiles());
            foreach (PayloadFile file in files)
            {
                string relativeWindowsPath = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                string destination = Path.GetFullPath(Path.Combine(target, relativeWindowsPath));
                if (!IsInsideTarget(target, destination))
                    throw new InvalidDataException("安装文件路径越界：" + file.RelativePath);

                string parent = Path.GetDirectoryName(destination);
                EnsureSafePayloadDestination(target, parent, destination);
                string temporary = destination + ".installing-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllBytes(temporary, file.Content);
                    File.Copy(temporary, destination, true);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch { }
                }
            }

            string executable = Path.Combine(target, Program.ProductExeName);
            if (createDesktopShortcut)
                Shortcut.Create(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), executable);
            if (createStartMenuShortcut)
            {
                string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Codex Account Dock");
                Shortcut.Create(menu, executable);
            }
        }

        internal static bool ContainsUnrelatedFiles(string targetDirectory)
        {
            if (!Directory.Exists(targetDirectory)) return false;
            HashSet<string> knownTopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CodexAccountDock.exe", "LICENSE", "README.md", "assets", "data", "backups", "source",
                "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "runtimes", "licenses"
            };
            return Directory.EnumerateFileSystemEntries(targetDirectory)
                .Select(Path.GetFileName)
                .Any(delegate(string name) { return !knownTopLevel.Contains(name); });
        }

        private static string ValidateTargetDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("请选择安装目录。");
            string target = Path.GetFullPath(directory.Trim());
            if (IsDriveRoot(target)) throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个专用文件夹。");
            if (target.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("不能安装到 Windows 系统目录。");

            DirectoryInfo info = Directory.CreateDirectory(target);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("安装目录不能是链接或联接点。");
            return target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void EnsureSafePayloadDestination(string target, string parent, string destination)
        {
            if ((new DirectoryInfo(target).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("安装目录不能是链接或联接点。");

            string relativeParent = parent.Length == target.Length ? "" : parent.Substring(target.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = target;
            if (relativeParent.Length > 0)
            {
                foreach (string part in relativeParent.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                {
                    if (part.Length == 0) continue;
                    current = Path.Combine(current, part);
                    if (!Directory.Exists(current)) Directory.CreateDirectory(current);
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("安装包目标目录不能是链接或联接点：" + current);
                }
            }

            if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("安装包目标文件不能是链接：" + destination);
        }

        private static bool IsDriveRoot(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsideTarget(string target, string path)
        {
            string prefix = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureWritable(string target)
        {
            string testFile = Path.Combine(target, ".write-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException("无法写入安装目录。请选择当前用户可写的文件夹。\n\n" + ex.Message, ex);
            }
        }

        private static void EnsureAppIsNotRunning(string target)
        {
            string expected = Path.GetFullPath(Path.Combine(target, Program.ProductExeName));
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Program.ProductExeName)))
            {
                try
                {
                    string running = Path.GetFullPath(process.MainModule.FileName);
                    if (string.Equals(running, expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Codex Account Dock 正在运行。请从托盘菜单退出它，再重新点击安装。安装程序不会关闭 Codex 桌面端。");
                }
                catch (InvalidOperationException) { throw; }
                catch { }
                finally { process.Dispose(); }
            }
        }
    }

    internal static class Shortcut
    {
        internal static void Create(string directory, string executable)
        {
            Directory.CreateDirectory(directory);
            string shortcutPath = Path.Combine(directory, "Codex Account Dock.lnk");
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows 快捷方式组件不可用。");

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { executable });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { Path.GetDirectoryName(executable) });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                    new object[] { executable + ",0" });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                    new object[] { "Codex 多账号本地管理工具" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    internal sealed class InstallerForm : Form
    {
        internal static int ProcessExitCode = 1;

        private readonly TextBox pathBox;
        private readonly Label diskLabel;
        private readonly CheckBox desktopShortcut;
        private readonly CheckBox startMenuShortcut;
        private readonly CheckBox launchAfterInstall;
        private readonly Button installButton;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width,Math.Max(1,area.Width-24)),Math.Min(Height,Math.Max(1,area.Height-24)));
            Location = new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        }

        internal InstallerForm()
        {
            AutoScaleDimensions = new SizeF(96F,96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            Text = "安装 Codex Account Dock";
            ClientSize = new Size(590, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(246, 248, 252);
            Icon = LoadIcon();

            Label title = new Label();
            title.Text = "Codex Account Dock";
            title.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(19, 31, 55);
            title.SetBounds(28, 24, 520, 42);
            Controls.Add(title);

            Label version = new Label();
            version.Text = "本地独立版  v" + Program.ProductVersion + "   ·   Windows / .NET Framework 4.6.2+";
            version.ForeColor = Color.FromArgb(83, 98, 124);
            version.SetBounds(31, 69, 520, 25);
            Controls.Add(version);

            Label pathLabel = new Label();
            pathLabel.Text = "安装位置";
            pathLabel.Font = new Font(Font, FontStyle.Bold);
            pathLabel.SetBounds(31, 113, 110, 24);
            Controls.Add(pathLabel);

            pathBox = new TextBox();
            pathBox.Text = Program.GetDefaultInstallDirectory();
            pathBox.SetBounds(31, 140, 440, 29);
            pathBox.TextChanged += delegate { UpdateDiskLabel(); };
            Controls.Add(pathBox);

            Button browse = new Button();
            browse.Text = "浏览…";
            browse.SetBounds(480, 138, 78, 32);
            browse.Click += BrowseClicked;
            Controls.Add(browse);

            diskLabel = new Label();
            diskLabel.ForeColor = Color.FromArgb(83, 98, 124);
            diskLabel.SetBounds(31, 176, 527, 23);
            Controls.Add(diskLabel);

            desktopShortcut = new CheckBox();
            desktopShortcut.Text = "创建桌面快捷方式";
            desktopShortcut.Checked = true;
            desktopShortcut.SetBounds(31, 218, 190, 27);
            Controls.Add(desktopShortcut);

            startMenuShortcut = new CheckBox();
            startMenuShortcut.Text = "创建开始菜单快捷方式";
            startMenuShortcut.Checked = true;
            startMenuShortcut.SetBounds(250, 218, 220, 27);
            Controls.Add(startMenuShortcut);

            launchAfterInstall = new CheckBox();
            launchAfterInstall.Text = "安装完成后启动（默认不启动）";
            launchAfterInstall.Checked = false;
            launchAfterInstall.SetBounds(31, 253, 290, 27);
            Controls.Add(launchAfterInstall);

            Label safety = new Label();
            safety.Text = "更新时会保留 data、账号密文和备份；安装程序不会关闭 Codex 或正在运行的 Account Dock。";
            safety.ForeColor = Color.FromArgb(83, 98, 124);
            safety.SetBounds(31, 291, 527, 42);
            Controls.Add(safety);

            installButton = new Button();
            installButton.Text = "安装";
            installButton.Font = new Font(Font, FontStyle.Bold);
            installButton.BackColor = Color.FromArgb(33, 94, 232);
            installButton.ForeColor = Color.White;
            installButton.FlatStyle = FlatStyle.Flat;
            installButton.FlatAppearance.BorderSize = 0;
            installButton.SetBounds(403, 337, 155, 38);
            installButton.Click += InstallClicked;
            Controls.Add(installButton);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(305, 337, 88, 38);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = installButton;
            CancelButton = cancel;
            UpdateDiskLabel();
        }

        private static Icon LoadIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return SystemIcons.Application; }
        }

        private void BrowseClicked(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 Codex Account Dock 的专用安装文件夹";
                dialog.SelectedPath = Directory.Exists(pathBox.Text) ? pathBox.Text : Program.GetDefaultInstallDirectory();
                dialog.ShowNewFolderButton = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.SelectedPath;
            }
        }

        private void UpdateDiskLabel()
        {
            try
            {
                string full = Path.GetFullPath(pathBox.Text.Trim());
                string root = Path.GetPathRoot(full);
                DriveInfo drive = new DriveInfo(root);
                diskLabel.Text = "目标磁盘 " + drive.Name + "，可用 " + FormatBytes(drive.AvailableFreeSpace) +
                    "（可点击浏览更改安装位置）";
            }
            catch
            {
                diskLabel.Text = "请输入有效的 Windows 文件夹路径。";
            }
        }

        private static string FormatBytes(long bytes)
        {
            double gb = bytes / 1024d / 1024d / 1024d;
            return gb.ToString("0.0") + " GB";
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            string target;
            try { target = Path.GetFullPath(pathBox.Text.Trim()); }
            catch (Exception ex)
            {
                MessageBox.Show("安装路径无效：" + ex.Message, "无法安装", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (InstallerEngine.ContainsUnrelatedFiles(target))
            {
                DialogResult answer = MessageBox.Show(
                    "所选目录包含其他文件。为了避免覆盖同名 README 或 LICENSE，建议选择一个专用文件夹。\n\n仍要安装到这里吗？",
                    "目录不是空的", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }

            installButton.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                InstallerEngine.Install(target, desktopShortcut.Checked, startMenuShortcut.Checked);
                ProcessExitCode = 0;
                if (launchAfterInstall.Checked)
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(target, Program.ProductExeName)) { UseShellExecute = true });
                }
                MessageBox.Show("Codex Account Dock 已安装完成。\n\n位置：" + target,
                    "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                installButton.Enabled = true;
            }
        }
    }
}

