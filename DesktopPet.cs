#if !NO_DESKTOP_PET
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexAccountDock
{
    internal static class DesktopPet
    {
        private sealed class PetGuideButton:Control
        {
            internal PetGuideButton(){DoubleBuffered=true;Cursor=Cursors.Hand;}
            protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<3||Height<3)return;using(var outline=GraphicsExtensions.CreateRoundedPath(ClientRectangle,14)){var previous=Region;Region=new System.Drawing.Region(outline);if(previous!=null)previous.Dispose();}}
            protected override void OnPaintBackground(PaintEventArgs e){}
            protected override void OnPaint(PaintEventArgs e)
            {
                var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var background=g.Save();g.TranslateTransform(-Left,-Top);InvokePaintBackground(Parent,new PaintEventArgs(g,new System.Drawing.Rectangle(Left,Top,Width,Height)));g.Restore(background);
                var box=new System.Drawing.Rectangle(1,1,Width-3,Height-3);
                using(var shape=GraphicsExtensions.CreateRoundedPath(box,14))
                using(var fill=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(205,ThemeVisual.Current.ButtonBottom)))
                using(var pen=new System.Drawing.Pen(ThemeVisual.Current.Rim,1)){g.FillPath(fill,shape);g.DrawPath(pen,shape);}
                AppTypography.Draw(g,Text,16,System.Drawing.FontStyle.Bold,box,System.Drawing.Color.White,System.Drawing.StringAlignment.Center,true);
            }
        }
        private static PetGuideButton GuideButton(string text,int x,int y,int width,int height){var button=new PetGuideButton {Text=text};button.SetBounds(x,y,width,height);return button;}
        private sealed class PetGuideWindow:DockedGlassWindow
        {
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                base.OnPaintBackground(e);
                var look=ThemeVisual.Current;var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var veil=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(105,look.PanelBottom)))g.FillRectangle(veil,ClientRectangle);
                foreach(var box in new[]{new System.Drawing.Rectangle(20,76,520,132)})
                using(var shape=GraphicsExtensions.CreateRoundedPath(box,18))
                using(var fill=new System.Drawing.Drawing2D.LinearGradientBrush(box,System.Drawing.Color.FromArgb(185,look.PanelTop),System.Drawing.Color.FromArgb(215,look.PanelBottom),90f))
                using(var edge=new System.Drawing.Pen(System.Drawing.Color.FromArgb(180,look.Rim),1))
                {g.FillPath(fill,shape);g.DrawPath(edge,shape);}
                using(var path=GraphicsExtensions.CreateRoundedPath(new System.Drawing.Rectangle(34,115,370,48),12))
                using(var fill=new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(165,look.PanelBottom)))
                using(var edge=new System.Drawing.Pen(System.Drawing.Color.FromArgb(185,look.Rim),1)){g.FillPath(fill,path);g.DrawPath(edge,path);}
            }
        }
        internal const string DownloadPage="https://github.com/Wunsch2048/codex-bailong-desktop-pet/releases/latest";
        private static string Root()
        {
            string nearby=FindRoot(Path.Combine(PortablePaths.Root,"桌宠"));if(nearby!=null)return nearby;
            string saved=Path.Combine(PortablePaths.Data,"desktop-pet-location.txt");
            if(File.Exists(saved))
            {string path=File.ReadAllText(saved).Trim();string found=FindRoot(path);if(found!=null)return found;}
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\BailongEnhanced"))
            {string value=key==null?null:key.GetValue("InstallLocation") as string;if(!string.IsNullOrWhiteSpace(value)&&Directory.Exists(value))return Path.GetFullPath(value);}
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","BailongEnhanced");
        }
        internal static string FindRoot(string folder)
        {
            if(string.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))return null;
            foreach(string path in new[]{folder,Path.Combine(folder,"LocalData","Programs","BailongEnhanced"),Path.Combine(folder,"Programs","BailongEnhanced")})
            {try{if(Executable(path)!=null)return Path.GetFullPath(path);}catch{}}
            return null;
        }
        internal static void Configure(IWin32Window owner)
        {
            using(var dialog=new FolderBrowserDialog {Description="选择 BailongPet 文件夹或白龙桌宠安装目录"})
            {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                string root=FindRoot(dialog.SelectedPath);
                if(root==null){MessageBox.Show(owner,"此目录没有有效的桌宠安装。请先运行一键部署包，再选择安装后的目录。","Codex 桌宠");return;}
                Directory.CreateDirectory(PortablePaths.Data);
                File.WriteAllText(Path.Combine(PortablePaths.Data,"desktop-pet-location.txt"),root);
                MessageBox.Show(owner,"已关联此电脑的桌宠。点击按钮开启，再次点击退出。","Codex 桌宠");
            }
        }
        internal static void MissingMenu(IWin32Window owner)
        {
            var menu=new ContextMenuStrip();
            menu.Items.Add("关联已安装的桌宠目录…",null,delegate{Configure(owner);});
            menu.Items.Add("安装桌宠到软件旁边…",null,delegate{Guide(owner);});
            menu.Items.Add("作者下载页 ↗",null,delegate{Process.Start(new ProcessStartInfo(DownloadPage){UseShellExecute=true});});
            menu.Closed+=delegate{menu.Dispose();};menu.Show(Cursor.Position);
        }
        internal static string Executable(string root)
        {
            string manifest=Path.Combine(root,"installations.json");
            if(!File.Exists(manifest))return null;
            var data=JsonUtil.Serializer.DeserializeObject(File.ReadAllText(manifest)) as System.Collections.Generic.Dictionary<string,object>;
            object raw;string current=data!=null&&data.TryGetValue("current",out raw)?raw as string:null;
            if(string.IsNullOrWhiteSpace(current)||current=="."||current==".."||current.IndexOfAny(Path.GetInvalidFileNameChars())>=0||current.Contains("/")||current.Contains("\\"))return null;
            string candidate=Path.Combine(root,"versions",current,"BailongEnhanced.exe");
            return File.Exists(candidate)?candidate:null;
        }
        private static ProcessStartInfo StartInfo(string executable,string arguments,string root)
        {
            var info=new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable)};
            // Portable installations keep their own LocalData environment.
            DirectoryInfo programs=Directory.GetParent(root);
            if(programs!=null&&programs.Parent!=null&&programs.Parent.Name.Equals("LocalData",StringComparison.OrdinalIgnoreCase))
            {
                string local=programs.Parent.FullName;info.EnvironmentVariables["LOCALAPPDATA"]=local;
                info.EnvironmentVariables["BAILONG_ISOLATED"]="1";
                string temp=Path.Combine(programs.Parent.Parent.FullName,"Temp");
                if(Directory.Exists(temp)){info.EnvironmentVariables["TEMP"]=temp;info.EnvironmentVariables["TMP"]=temp;}
            }
            return info;
        }
        internal static void Toggle(IWin32Window owner,bool preview)
        {
            if(preview)return;
            try
            {
                string root=Root(),exe=Executable(root);
                if(exe==null)
                {
                    Guide(owner);
                    return;
                }
                bool running=false;
                foreach(Process process in Process.GetProcessesByName("BailongEnhanced"))
                    using(process){try{string path=process.MainModule.FileName;if(path.StartsWith(Path.Combine(root,"versions")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){running=true;break;}}catch{}}
                using(Process process=Process.Start(StartInfo(exe,running?"--quit":"",root))){}
            }
            catch(Exception error){MessageBox.Show(owner,"桌宠操作未完成："+error.Message,"Codex 桌宠",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        internal static ProcessStartInfo InstallerInfo(string installer,string folder)
        {
            var info=new ProcessStartInfo(installer){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(installer)};
            info.EnvironmentVariables["LOCALAPPDATA"]=Path.Combine(folder,"LocalData");
            info.EnvironmentVariables["TEMP"]=Path.Combine(folder,"Temp");info.EnvironmentVariables["TMP"]=Path.Combine(folder,"Temp");
            info.EnvironmentVariables["BAILONG_ISOLATED"]="1";
            return info;
        }
        internal static void Guide(IWin32Window owner,bool diagnostic=false)
        {
            using(var form=new PetGuideWindow {Text="安装 Codex 桌宠",ClientSize=new System.Drawing.Size(560,416),StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,AutoScaleMode=AutoScaleMode.Dpi})
            using(var timer=new Timer {Interval=1500})
            {
                string target=Path.Combine(PortablePaths.Root,"桌宠");Process installing=null;bool completed=false;
                var title=new Label {Text="Codex 桌宠",Left=26,Top=22,Width=500,Height=28,Font=Palette.Font(13,true)};
                var path=new Label {Text=target,Left=44,Top=121,Width=350,Height=36,AutoEllipsis=true,TextAlign=System.Drawing.ContentAlignment.MiddleLeft,Font=Palette.Font(10,false),BackColor=System.Drawing.Color.Transparent,ForeColor=Palette.Ink};
                var status=new Label {Text="已内置白龙桌宠 1.0.0，无需打开网页下载。",Left=36,Top=174,Width=480,Height=25};
                var pathCaption=new Label {Text="安装位置",Left=36,Top=87,Width=460,Height=24,Font=Palette.Font(11,true)};
                var optionsCaption=new Label {Text="安装与下载",Left=28,Top=226,Width=460,Height=25,Font=Palette.Font(11,true)};
                var download=GuideButton("作者下载 ↗",28,264,150,40);
                var install=GuideButton("安装内置桌宠",190,264,198,40);
                var browse=GuideButton("更换目录",414,121,108,36);path.Width=350;
                var check=GuideButton("重新检测",400,264,132,40);
                var note=new Label {Text="按原版安装器完成安装，随后自动检测并关联。",Left=28,Top=318,Width=500,Height=28};
                form.Controls.AddRange(new Control[]{title,pathCaption,path,status,optionsCaption,download,install,browse,check,note});
                Palette.UpdateControls(form);
                var pathTip=new ToolTip {AutoPopDelay=20000};pathTip.SetToolTip(path,target);path.Text=target;
                form.FormClosed+=delegate{pathTip.Dispose();};
                foreach(Control control in form.Controls){if(control is Label){control.BackColor=System.Drawing.Color.Transparent;control.ForeColor=Palette.Ink;}if(control is Button){control.BackColor=ThemeVisual.Current.ButtonBottom;control.ForeColor=System.Drawing.Color.White;control.Font=Palette.Font(11,true);((Button)control).FlatStyle=FlatStyle.Flat;((Button)control).FlatAppearance.BorderColor=ThemeVisual.Current.Rim;}}
                var close=GuideButton("关闭",428,366,104,32);close.Click+=delegate{form.Close();};form.Controls.Add(close);
                var closeTop=GuideButton("×",500,18,34,34);closeTop.Click+=delegate{form.Close();};form.Controls.Add(closeTop);closeTop.BringToFront();title.Width=460;
                form.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)Native.BeginWindowDrag(form.Handle);};
                Action detect=delegate
                {
                    if(completed)return;string root=FindRoot(target);
                    if(root==null || File.Exists(Path.Combine(root,"deployment-journal.json")))return;
                    if(installing!=null){try{if(!installing.HasExited)return;}catch{}}
                    Directory.CreateDirectory(PortablePaths.Data);File.WriteAllText(Path.Combine(PortablePaths.Data,"desktop-pet-location.txt"),root);
                    completed=true;status.Text="已自动关联 · 关闭此页后，点击桌宠按钮开启／退出。";install.Enabled=false;browse.Enabled=false;timer.Stop();
                };
                download.Click+=delegate{Process.Start(new ProcessStartInfo(DownloadPage){UseShellExecute=true});};
                browse.Click+=delegate{using(var dialog=new FolderBrowserDialog {Description="选择桌宠专用子目录",SelectedPath=target})if(dialog.ShowDialog(form)==DialogResult.OK){target=dialog.SelectedPath;path.Text=target;pathTip.SetToolTip(path,target);}};
                install.Click+=delegate
                {
                    using(var dialog=new OpenFileDialog {Title="选择作者提供的白龙桌宠安装程序",Filter="安装程序 (*.exe)|*.exe",CheckFileExists=true})
                    {
                        string installer=Path.Combine(PortablePaths.Root,"extras","BailongSetup.exe");
                        if(!File.Exists(installer)){if(dialog.ShowDialog(form)!=DialogResult.OK)return;installer=dialog.FileName;}
                        if(MessageBox.Show(form,"将运行："+installer+"\r\n安装位置："+target,"确认安装桌宠",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                        try
                        {
                            Directory.CreateDirectory(target);string probe=Path.Combine(target,".write-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);
                            if(Path.GetFileName(installer)=="BailongSetup.exe"){using(var stream=File.OpenRead(installer))using(var sha=System.Security.Cryptography.SHA256.Create()){if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!="78677B578DAD02C89C30704BDE6852CBED0503BAD84455954DA339F103F778BF")throw new InvalidDataException("内置安装包校验失败，请重新安装账号助手。");}}
                            var info=InstallerInfo(installer,target);Directory.CreateDirectory(info.EnvironmentVariables["LOCALAPPDATA"]);Directory.CreateDirectory(info.EnvironmentVariables["TEMP"]);
                            installing=Process.Start(info);Directory.CreateDirectory(PortablePaths.Data);File.WriteAllText(Path.Combine(PortablePaths.Data,"desktop-pet-location.txt"),target);install.Enabled=false;browse.Enabled=false;status.Text="安装程序已打开。请完成安装；完成后将自动检测并关联。";
                        }
                        catch(Exception error){status.Text="无法安装："+error.Message+"。可更换到可写目录后重试。";install.Enabled=true;browse.Enabled=true;}
                    }
                };
                check.Click+=delegate{detect();if(!completed){status.Text="尚未检测到完整安装。请完成安装，或右键桌宠按钮关联实际安装目录。";try{if(installing==null||installing.HasExited){install.Enabled=true;browse.Enabled=true;}}catch{}}};
                timer.Tick+=delegate{try{detect();}catch(Exception error){status.Text="检测未完成："+error.Message;}};
                form.Shown+=delegate{if(diagnostic){form.BeginInvoke((Action)delegate{foreach(Control action in new Control[]{closeTop,download,install,check,close}){if(!form.ClientRectangle.Contains(action.Bounds)||!form.Region.IsVisible(action.Left+2,action.Top+action.Height/2)||!form.Region.IsVisible(action.Right-3,action.Top+action.Height/2))throw new InvalidOperationException("操作按钮被窗口边界裁剪");}string output=Path.Combine(PortablePaths.Root,"test-output");Directory.CreateDirectory(output);using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"desktop-pet-guide.png"));}form.Close();});}else{timer.Start();detect();}};form.FormClosed+=delegate{timer.Stop();if(installing!=null)installing.Dispose();};
                form.ShowDialog(owner);
            }
        }
        internal static int Diagnostic()
        {
            string dir=Path.Combine(Path.GetTempPath(),"AccountDock-pet-fixture-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string manifest=Path.Combine(dir,"installations.json");
                foreach(string current in new[]{"../escape","..","a/b","a\\b"})
                {File.WriteAllText(manifest,JsonUtil.Serializer.Serialize(new{current=current}));if(Executable(dir)!=null)throw new Exception("Manifest traversal accepted");}
                string version=Path.Combine(dir,"versions","fixture");Directory.CreateDirectory(version);
                File.WriteAllText(Path.Combine(version,"BailongEnhanced.exe"),"synthetic-not-executable");
                File.WriteAllText(manifest,"{\"current\":\"fixture\"}");
                string exe=Executable(dir);if(exe!=Path.Combine(version,"BailongEnhanced.exe"))throw new Exception("Current version resolution");
                if(FindRoot(dir)!=Path.GetFullPath(dir))throw new Exception("Installed root discovery");
                var installerInfo=InstallerInfo(exe,dir);
                if(installerInfo.UseShellExecute || installerInfo.EnvironmentVariables["LOCALAPPDATA"]!=Path.Combine(dir,"LocalData") || installerInfo.EnvironmentVariables["BAILONG_ISOLATED"]!="1")throw new Exception("Installer isolated environment");
                var quit=StartInfo(exe,"--quit",dir);
                if(quit.Arguments!="--quit"||quit.UseShellExecute||!quit.CreateNoWindow)throw new Exception("Quit command");
                var normal=SidePanels.DockBounds(new System.Drawing.Rectangle(1300,0,573,860),new System.Drawing.Rectangle(0,0,1920,1080));
                var large=SidePanels.DockBounds(new System.Drawing.Rectangle(2000,0,860,1290),new System.Drawing.Rectangle(0,0,3840,2160));
                if(normal.Width!=720||large.Width<=720||large.Width>1080)throw new Exception("Sidebar proportional width");
                Console.WriteLine("DESKTOP_PET_TEST_OK manifest_path_validation current_version exact_quit_command proportional_sidebar no_launch no_termination no_credentials no_network");return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
            finally{try{Directory.Delete(dir,true);}catch{}}
        }
    }
}

#endif
