using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace CodexAccountDock
{
    /// <summary>
    /// Locates an existing official Codex CLI and gives the user an explicit,
    /// visible installation path when it is missing.  Discovery only inspects
    /// local files; it never starts a process or connects to the network.
    /// </summary>
    internal static class CliSetup
    {
        private const string NodeDownloadUrl = "https://nodejs.org/en/download";
        private const string CliDocumentationUrl = "https://developers.openai.com/codex/cli/";
        private const string InstallCommand = "npm install -g @openai/codex@latest";

        internal static int RunVisualDiagnostic()
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var dialog=new SetupDialog())
            {
                dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-20000,-20000);
                dialog.Show();dialog.Update();Application.DoEvents();
                foreach(Control child in dialog.Controls)
                    if(child.Bottom>dialog.ClientSize.Height || child.Right>dialog.ClientSize.Width || child.Font.SizeInPoints<11.5f)throw new Exception("Setup text bounds/size");
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(folder);
                using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,dialog.ClientRectangle);image.Save(Path.Combine(folder,"cli-setup.png"));}
                dialog.Close();
            }
            Console.WriteLine("CLI_SETUP_VISUAL_OK readable_labels bounds no_install no_network");return 0;
        }

        internal static string Find()
        {
            foreach (string candidate in Candidates())
            {
                try
                {
                    if (IsLocalPath(candidate) && File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Returns the discovered command, or shows a themed setup guide and
        /// returns the command found by the user's later recheck.
        /// </summary>
        internal static string EnsureAvailable(IWin32Window owner)
        {
            string found = Find();
            if (!string.IsNullOrEmpty(found)) return found;
            using (SetupDialog dialog = new SetupDialog())
            {
                dialog.ShowDialog(owner);
                return dialog.FoundPath;
            }
        }

        /// <summary>Offline test hook.  It neither installs nor opens anything.</summary>
        internal static bool SelfTest()
        {
            foreach (string path in Candidates())
                if (!string.IsNullOrWhiteSpace(path) && !IsLocalPath(path)) return false;
            return InstallCommand == "npm install -g @openai/codex@latest" &&
                NodeDownloadUrl.StartsWith("https://", StringComparison.Ordinal) &&
                CliDocumentationUrl.StartsWith("https://", StringComparison.Ordinal);
        }

        private static IEnumerable<string> Candidates()
        {
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string npmRoot = string.IsNullOrWhiteSpace(roaming) ? null : Path.Combine(roaming, "npm");

            // The npm launcher is a small wrapper.  Prefer its packaged native
            // executable so callers do not need cmd.exe or PowerShell quoting.
            foreach (string native in NpmNativeCandidates(npmRoot))
                if (emitted.Add(native)) yield return native;

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                string desktopBin = Path.Combine(local, "OpenAI", "Codex", "bin");
                if (IsLocalPath(desktopBin) && Directory.Exists(desktopBin))
                {
                    string[] versions = SafeDirectories(desktopBin);
                    Array.Sort(versions, StringComparer.OrdinalIgnoreCase);
                    Array.Reverse(versions);
                    foreach (string version in versions)
                    {
                        string native = Path.Combine(version, "codex.exe");
                        if (emitted.Add(native)) yield return native;
                    }
                    string direct = Path.Combine(desktopBin, "codex.exe");
                    if (emitted.Add(direct)) yield return direct;
                }
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string raw in pathValue.Split(Path.PathSeparator))
            {
                string folder = NormalizeFolder(raw);
                if (string.IsNullOrEmpty(folder) || !IsLocalPath(folder)) continue;
                foreach (string name in new[] { "codex.exe", "codex.cmd", "codex.bat", "codex.ps1" })
                {
                    string candidate;
                    try { candidate = Path.Combine(folder, name); }
                    catch { continue; }
                    if (emitted.Add(candidate)) yield return candidate;
                }
            }

            if (!string.IsNullOrEmpty(npmRoot) && IsLocalPath(npmRoot))
            {
                foreach (string name in new[] { "codex.exe", "codex.cmd", "codex.bat", "codex.ps1" })
                {
                    string candidate = Path.Combine(npmRoot, name);
                    if (emitted.Add(candidate)) yield return candidate;
                }
            }
        }

        private static IEnumerable<string> NpmNativeCandidates(string npmRoot)
        {
            if (string.IsNullOrEmpty(npmRoot) || !IsLocalPath(npmRoot)) yield break;
            string packages = Path.Combine(npmRoot, "node_modules", "@openai", "codex", "node_modules", "@openai");
            if (!Directory.Exists(packages)) yield break;
            foreach (string package in SafeDirectories(packages, "codex-win32-*"))
            {
                string vendor = Path.Combine(package, "vendor");
                foreach (string target in SafeDirectories(vendor))
                    yield return Path.Combine(target, "bin", "codex.exe");
            }
        }

        private static string[] SafeDirectories(string root)
        {
            return SafeDirectories(root, "*");
        }

        private static string[] SafeDirectories(string root, string pattern)
        {
            try
            {
                if (!IsLocalPath(root) || !Directory.Exists(root)) return new string[0];
                return Directory.GetDirectories(root, pattern, SearchOption.TopDirectoryOnly);
            }
            catch { return new string[0]; }
        }

        private static string NormalizeFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string folder = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            try { return Path.GetFullPath(folder); }
            catch { return null; }
        }

        private static bool IsLocalPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            try
            {
                string full = Path.GetFullPath(value);
                string root = Path.GetPathRoot(full);
                return !string.IsNullOrWhiteSpace(root) && root.Length >= 2 && root[1] == ':';
            }
            catch { return false; }
        }

        private static string FindNpm()
        {
            List<string> folders = new List<string>();
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string raw in pathValue.Split(Path.PathSeparator))
            {
                string folder = NormalizeFolder(raw);
                if (!string.IsNullOrEmpty(folder) && IsLocalPath(folder)) folders.Add(folder);
            }
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(roaming)) folders.Add(Path.Combine(roaming, "npm"));
            foreach (string folder in folders)
            {
                foreach (string name in new[] { "npm.cmd", "npm.exe" })
                {
                    try
                    {
                        string file = Path.Combine(folder, name);
                        if (File.Exists(file)) return Path.GetFullPath(file);
                    }
                    catch { }
                }
            }
            return null;
        }

        private static void OpenWebPage(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开浏览器：" + ex.Message, "Codex CLI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private sealed class SetupDialog : Form, IPlanBackdropLayer
        {
            private readonly Label status;
            private readonly PlanGlassButton install;
            internal string FoundPath { get; private set; }

            internal SetupDialog()
            {
                Text = "准备 Codex CLI";
                ClientSize = new Size(586, 400);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                MaximizeBox = false;
                MinimizeBox = false;
                AutoScaleMode = AutoScaleMode.Dpi;
                DoubleBuffered = true;
                BackColor = ThemeVisual.Current.PanelBottom;
                ForeColor = Color.White;
                Padding = new Padding(24);

                Label title = MakeLabel("需要 Codex CLI", 25, true);
                title.SetBounds(28, 22, 470, 38);
                Controls.Add(title);

                Label description = MakeLabel(
                    "添加账号和错峰计划的独立授权，需要官方 Codex CLI。\r\n程序不会自动安装；请由你选择安装方式。", 15, false);
                description.SetBounds(30, 68, 525, 56);
                Controls.Add(description);

                status = MakeLabel("正在检查这台电脑……", 14, true);
                status.SetBounds(30, 135, 525, 48);
                Controls.Add(status);

                PlanGlassButton recheck = Button("重新检测", 30, 205, 164);
                recheck.Emphasized = true;
                recheck.Click += delegate { Recheck(); };
                Controls.Add(recheck);

                install = Button("安装 Codex CLI", 206, 205, 176);
                install.Click += delegate { StartInstall(); };
                Controls.Add(install);

                PlanGlassButton node = Button("安装 Node.js", 394, 205, 162);
                node.Click += delegate { OpenWebPage(NodeDownloadUrl); };
                Controls.Add(node);

                PlanGlassButton docs = Button("查看官方说明", 30, 269, 164);
                docs.Click += delegate { OpenWebPage(CliDocumentationUrl); };
                Controls.Add(docs);

                PlanGlassButton cancel = Button("取消", 394, 269, 162);
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(cancel);

                Label hint = MakeLabel("安装窗口会保持打开。安装完成后回到这里点“重新检测”，无需重启程序。", 12, false);
                hint.SetBounds(30, 321, 525, 56);
                hint.TextAlign = ContentAlignment.MiddleCenter;
                Controls.Add(hint);

                Shown += delegate { UpdateInstallState(); };
                Resize += delegate { UpdateRegion(); };
                UpdateRegion();
            }

            private static Label MakeLabel(string text, int pixels, bool bold)
            {
                return new Label
                {
                    Text = text,
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Font = new Font("楷体", Math.Max(16,pixels), bold ? FontStyle.Bold : FontStyle.Bold, GraphicsUnit.Pixel),
                    AutoSize = false
                };
            }

            private static PlanGlassButton Button(string text, int x, int y, int width)
            {
                return new PlanGlassButton
                {
                    Text = text,
                    Bounds = new Rectangle(x, y, width, 44),
                    Radius = 18,
                    Font = new Font("楷体", 16, FontStyle.Bold, GraphicsUnit.Pixel)
                };
            }

            private void UpdateInstallState()
            {
                string npm = FindNpm();
                install.Enabled = !string.IsNullOrEmpty(npm);
                status.Text = install.Enabled
                    ? "尚未找到 Codex CLI。已检测到 npm，可以打开安装窗口。"
                    : "尚未找到 Codex CLI，也没有检测到 npm。请先安装 Node.js。";
            }

            private void Recheck()
            {
                string found = Find();
                if (!string.IsNullOrEmpty(found))
                {
                    FoundPath = found;
                    status.Text = "已找到 Codex CLI，可以继续添加账号。";
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                UpdateInstallState();
            }

            private void StartInstall()
            {
                string npm = FindNpm();
                if (string.IsNullOrEmpty(npm))
                {
                    UpdateInstallState();
                    return;
                }
                try
                {
                    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    string powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
                    if (!File.Exists(powershell)) powershell = "powershell.exe";
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = powershell,
                        Arguments = "-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -Command \"Write-Host '正在安装官方 Codex CLI…' -ForegroundColor Cyan; & $env:CODEX_DOCK_NPM install -g @openai/codex@latest; if ($LASTEXITCODE -eq 0) { Write-Host '安装完成。请回到账号工具点击“重新检测”。' -ForegroundColor Green } else { Write-Host '安装没有完成，请查看上方提示。' -ForegroundColor Yellow }\"",
                        UseShellExecute = false,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    };
                    psi.EnvironmentVariables["CODEX_DOCK_NPM"] = npm;
                    Process.Start(psi);
                    status.Text = "安装窗口已打开。完成后点击“重新检测”。";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "无法打开安装窗口：" + ex.Message, "Codex CLI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            private void UpdateRegion()
            {
                if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
                if (Region != null) Region.Dispose();
                using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 24))
                    Region = new Region(path);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                GlassBackdrop.DrawReferenceBackground(e.Graphics, ClientRectangle,false);
                PaintSetupOverlay(e.Graphics);
            }
            void IPlanBackdropLayer.PaintPlanBackdropLayer(Graphics graphics,Rectangle bounds){PaintSetupOverlay(graphics);}
            private void PaintSetupOverlay(Graphics graphics)
            {
                ThemeLook look = ThemeVisual.Current;
                using (SolidBrush veil = new SolidBrush(Color.FromArgb(205, look.PanelBottom)))
                    graphics.FillRectangle(veil, ClientRectangle);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = GraphicsExtensions.CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 24))
                using (Pen rim = new Pen(Color.FromArgb(235, look.Rim), 1.3f))
                    graphics.DrawPath(rim, path);
            }
        }
    }
}
