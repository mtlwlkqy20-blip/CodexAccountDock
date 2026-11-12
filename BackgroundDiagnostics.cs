using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    internal static class BackgroundDiagnostics
    {
        private sealed class TestWindow:DockedGlassWindow{}
        internal static int Run(string file)
        {
            // A diagnostic must never replace a real user's saved background.
            if(File.Exists(PortablePaths.Settings) || File.Exists(Path.Combine(PortablePaths.Data,"background-media.txt")) || File.Exists(Path.Combine(PortablePaths.Data,"custom-background.png")))
            {Console.WriteLine("BACKGROUND_TEST_REFUSED real_or_existing_data");return 2;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            int result=1;bool finished=false;
            using(var window=new TestWindow {ClientSize=new Size(480,720),Text="高清背景诊断（无账号）"})
            using(var deadline=new System.Windows.Forms.Timer {Interval=45000})
            {
                deadline.Tick+=delegate{Console.WriteLine("BACKGROUND_FAILED deadline");window.Close();};
                window.Shown+=async delegate
                {
                    deadline.Start();
                    try
                    {
                        ThemeSelection.Select("custom",false);
                        Color first;
                        using(var image=new Bitmap(80,80))using(Graphics g=Graphics.FromImage(image))
                        {
                            g.Clear(Color.Crimson);Palette.FromBackground(image);first=Palette.GlassBottom;
                            g.Clear(Color.RoyalBlue);Palette.FromBackground(image);
                            if(first==Palette.GlassBottom || Palette.GlassBottom.GetBrightness()>.35f)throw new Exception("Adaptive color/contrast");
                            g.Clear(Color.White);Palette.FromBackground(image);
                            if(Palette.GlassBottom.GetBrightness()>.35f)throw new Exception("Neutral contrast");
                        }
                        await DynamicBackground.ImportAsync(Path.GetFullPath(file));
                        ThemeSelection.Select("custom",false);
                        if(ThemeVisual.Current.PanelBottom!=Palette.GlassBottom)throw new Exception("Imported video did not use its adaptive palette");
                        long before=DynamicBackground.FrameVersion;
                        await Task.Delay(5200);
                        if(DynamicBackground.LastError!=null)throw new Exception(DynamicBackground.LastError);
                        long advance=DynamicBackground.FrameVersion-before;
                        if(advance<3)throw new Exception("Video did not advance: "+advance);
                        string output=Path.Combine(PortablePaths.Root,"test-output");Directory.CreateDirectory(output);
                        using(var shot=new Bitmap(480,720)){window.DrawToBitmap(shot,window.ClientRectangle);shot.Save(Path.Combine(output,"dynamic-background.png"),ImageFormat.Png);}
                        var preview=new AccountStore(true);long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        preview.Settings.Accounts.Add(new AccountMeta {Id="sample",Email="demo@example.com",Plan="plus",PrimaryRemaining=70,SecondaryRemaining=37,PrimaryResetAt=now+7200,SecondaryResetAt=now+86400});preview.PreviewActiveId="sample";
                        using(var account=new AccountPopup(null,preview))
                        {
                            account.Show();await Task.Delay(500);account.SavePreview(Path.Combine(output,"dynamic-account.png"));account.AllowExit=true;account.Close();
                        }
                        using(var news=new DailyNews.NewsWindow(null))
                        {
                            news.Canvas.SetItems(new[]{new NewsItem {Id="test",Title="自适应配色测试 · 演示数据",Summary="背景的视频画面变化，卡片和按钮保持导入时的主色，不逐帧闪变。",Category="产品",Source="离线演示",Stamp="",OriginalUrl=""}});
                            news.Show();await Task.Delay(400);using(var shot=new Bitmap(news.ClientSize.Width,news.ClientSize.Height)){news.DrawToBitmap(shot,news.ClientRectangle);shot.Save(Path.Combine(output,"dynamic-news.png"),ImageFormat.Png);}news.Close();
                        }
                        using(var current=Process.GetCurrentProcess()){current.Refresh();Console.WriteLine("BACKGROUND_MEMORY working_set_mib="+(current.WorkingSet64/1048576.0).ToString("0.0"));}
                        DynamicBackground.TogglePause();long held=DynamicBackground.FrameVersion;
                        await Task.Delay(400);if(DynamicBackground.FrameVersion!=held)throw new Exception("Pause did not stop redraw");
                        DynamicBackground.TogglePause();await Task.Delay(300);
                        window.Hide();held=DynamicBackground.FrameVersion;
                        await Task.Delay(500);if(DynamicBackground.FrameVersion!=held)throw new Exception("Hidden video did not pause");
                        window.Show();await Task.Delay(550);
                        if(DynamicBackground.FrameVersion<=held)throw new Exception("Hidden resume failed");
                        ThemeSelection.Select("summer",false);held=DynamicBackground.FrameVersion;
                        await Task.Delay(400);if(DynamicBackground.FrameVersion!=held)throw new Exception("Built-in theme still decoding");
                        ThemeSelection.Select("custom",false);await Task.Delay(500);
                        string previous=DynamicBackground.PosterPath;
                        bool rejected=false;
                        try{await DynamicBackground.ImportAsync(Path.Combine(output,"missing.mp4"));}catch(IOException){rejected=true;}
                        if(!rejected || DynamicBackground.PosterPath!=previous)throw new Exception("Invalid import changed original");
                        string still=Path.Combine(output,"green-test.png");
                        using(var image=new Bitmap(1600,900))using(Graphics g=Graphics.FromImage(image)){g.Clear(Color.SeaGreen);image.Save(still,ImageFormat.Png);}
                        await DynamicBackground.ImportAsync(still);ThemeSelection.Select("custom",false);
                        if(DynamicBackground.HasVideo)throw new Exception("Static switch did not stop video");
                        if(File.Exists(Path.Combine(PortablePaths.Data,"backgrounds",Path.GetFileNameWithoutExtension(previous)+".mp4")))throw new Exception("Replaced video retained");
                        await DynamicBackground.RestoreAsync();ThemeSelection.Select("heritage",false);
                        if(DynamicBackground.HasVideo)throw new Exception("Restore retained video");
                        Console.WriteLine("BACKGROUND_OK hd_decode loop adaptive_color contrast pause hidden_resume builtin_stop invalid_preserves static_switch restore no_network no_accounts frames="+advance);
                        result=0;finished=true;
                    }
                    catch(Exception ex){Console.WriteLine("BACKGROUND_FAILED "+ex);}
                    finally{deadline.Stop();DynamicBackground.Shutdown();window.Close();}
                };
                Application.Run(window);
            }
            return finished?result:1;
        }
    }
}
