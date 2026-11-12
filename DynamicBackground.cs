using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexAccountDock
{
    // One shared, silent decoder; at most one pending frame, never a frame list.
    internal static class DynamicBackground
    {
        private sealed class Asset {internal string Id,Kind,Poster,Video;}
        private static readonly List<Form> Windows=new List<Form>();
        private static readonly object PendingLock=new object();
        private static readonly ManualResetEventSlim PlayGate=new ManualResetEventSlim(false);
        private static System.Windows.Forms.Timer timer;
        private static CancellationTokenSource playback;
        private static Task worker=Task.FromResult(true);
        private static Bitmap pending;
        private static Asset asset;
        private static bool loaded,paused,pauseLoaded,shutdown;
        private static volatile bool failed;
        private static int generation;
        internal static long FrameVersion {get;private set;}
        internal static string LastError {get;private set;}
        private static string Manifest {get{return Path.Combine(PortablePaths.Data,"background-media.txt");}}
        private static string AssetFolder {get{return Path.Combine(PortablePaths.Data,"backgrounds");}}
        internal static string PosterPath {get{EnsureAsset();return asset==null?Path.Combine(PortablePaths.Data,"custom-background.png"):asset.Poster;}}
        internal static bool HasVideo {get{EnsureAsset();return asset!=null && asset.Kind=="video" && File.Exists(asset.Video);}}
        internal static bool Paused {get{EnsurePause();return paused;}}

        internal static void Attach(Form window)
        {
            if(Windows.Contains(window))return;
            Windows.Add(window);
            if(timer==null){timer=new System.Windows.Forms.Timer {Interval=66};timer.Tick+=delegate{Pump();};}
            window.VisibleChanged+=delegate{UpdatePlayback();};
            window.Resize+=delegate{UpdatePlayback();};
            window.Disposed+=delegate{Windows.Remove(window);UpdatePlayback();};
            UpdatePlayback();
        }

        private static void EnsurePause()
        {
            if(pauseLoaded)return;pauseLoaded=true;
            try{paused=File.ReadAllText(Path.Combine(PortablePaths.Data,"background-playback.txt")).Trim()=="paused";}catch{paused=false;}
        }

        private static void EnsureAsset()
        {
            if(loaded)return;loaded=true;asset=null;
            try
            {
                var info=new FileInfo(Manifest);if(!info.Exists || info.Length>256)return;
                string[] lines=File.ReadAllLines(Manifest);Guid id;
                if(lines.Length!=3 || lines[0]!="v1" || (lines[1]!="image" && lines[1]!="video") || !Guid.TryParseExact(lines[2],"N",out id))return;
                var next=new Asset {Id=id.ToString("N"),Kind=lines[1]};
                next.Poster=Path.Combine(AssetFolder,next.Id+".png");next.Video=Path.Combine(AssetFolder,next.Id+".mp4");
                if(File.Exists(next.Poster) && (next.Kind!="video" || File.Exists(next.Video)))asset=next;
            }
            catch{asset=null;}
        }

        internal static void Reload()
        {
            StopWorker();loaded=false;asset=null;failed=false;LastError=null;FrameVersion++;
            UpdatePlayback();
        }

        internal static void TogglePause()
        {
            EnsurePause();bool next=!paused;
            Directory.CreateDirectory(PortablePaths.Data);
            AtomicText(Path.Combine(PortablePaths.Data,"background-playback.txt"),next?"paused":"playing");
            paused=next;UpdatePlayback();
        }

        private static void UpdatePlayback()
        {
            if(shutdown)return;
            EnsurePause();
            bool visible=Windows.Any(f=>!f.IsDisposed && f.Visible && f.WindowState!=FormWindowState.Minimized);
            bool active=visible && ThemeSelection.Current=="custom" && HasVideo && !paused && !failed;
            if(active)
            {
                if(playback==null)
                {
                    string path=asset.Video;int version=++generation;
                    playback=new CancellationTokenSource();CancellationToken token=playback.Token;
                    worker=worker.ContinueWith(delegate {DecodeLoop(path,version,token);},CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
                }
                PlayGate.Set();if(timer!=null)timer.Start();
            }
            else {PlayGate.Reset();if(timer!=null)timer.Stop();}
        }

        private static void DecodeLoop(string path,int version,CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using(var decoder=new MediaVideoDecoder(path))
                {
                    var clock=Stopwatch.StartNew();long origin=-1,lastPublished=-10000000;
                    while(true)
                    {
                        token.ThrowIfCancellationRequested();
                        if(!PlayGate.IsSet){PlayGate.Wait(token);clock.Restart();origin=-1;lastPublished=-10000000;}
                        long stamp;using(Bitmap frame=decoder.ReadFrame(out stamp))
                        {
                            if(frame==null){decoder.Rewind();origin=-1;lastPublished=-10000000;clock.Restart();token.WaitHandle.WaitOne(20);continue;}
                            if(origin<0)origin=stamp;
                            long due=Math.Max(0,stamp-origin),wait=due-clock.Elapsed.Ticks;
                            if(wait>0 && wait<TimeSpan.FromSeconds(10).Ticks)
                            {
                                if(token.WaitHandle.WaitOne((int)Math.Min(10000,wait/TimeSpan.TicksPerMillisecond)))token.ThrowIfCancellationRequested();
                            }
                            if(!PlayGate.IsSet)continue;
                            if(due-lastPublished<TimeSpan.FromMilliseconds(60).Ticks)continue;
                            lastPublished=due;
                            Bitmap copy=FitFrame(frame,1920);
                            lock(PendingLock)
                            {
                                if(token.IsCancellationRequested || version!=generation){copy.Dispose();return;}
                                if(pending!=null)pending.Dispose();pending=copy;
                            }
                        }
                    }
                }
            }
            catch(OperationCanceledException){}
            catch(Exception)
            {
                lock(PendingLock){if(version==generation){failed=true;LastError="动态背景无法播放，已保留静态封面。请使用H.264编码的MP4；Windows N版本需安装媒体功能包。";}}
            }
        }

        private static void Pump()
        {
            if(failed){UpdatePlayback();return;}
            Bitmap next;
            lock(PendingLock){next=pending;pending=null;}
            if(next==null)return;
            if(ThemeSelection.Current!="custom" || !HasVideo){next.Dispose();return;}
            GlassBackdrop.SetAnimatedFrame(next);FrameVersion++;
            foreach(Form form in Windows.ToArray())if(!form.IsDisposed && form.Visible && form.WindowState!=FormWindowState.Minimized)form.Invalidate(true);
        }

        private static void StopWorker()
        {
            generation++;
            CancellationTokenSource previous=playback;playback=null;
            if(previous!=null){previous.Cancel();Task finishing=worker;finishing.ContinueWith(delegate{previous.Dispose();},TaskScheduler.Default);}
            PlayGate.Set();if(timer!=null)timer.Stop();
            lock(PendingLock){if(pending!=null)pending.Dispose();pending=null;}
        }

        private static async Task StopAndWait()
        {
            StopWorker();Task finishing=worker;
            if(await Task.WhenAny(finishing,Task.Delay(10000))!=finishing)throw new IOException("背景视频仍在释放，请稍后重试。");
            await finishing;
        }

        internal static async Task ImportAsync(string input)
        {
            string full=Path.GetFullPath(input);var file=new FileInfo(full);
            if(!file.Exists || file.Length==0)throw new IOException("背景文件不存在或为空。");
            bool video=string.Equals(file.Extension,".mp4",StringComparison.OrdinalIgnoreCase);
            if(file.Length>(video?300L*1024*1024:50L*1024*1024))throw new IOException(video?"请选择300MB以内的MP4背景。":"请选择50MB以内的图片背景。");
            if(!video && !new[]{".jpg",".jpeg",".png",".bmp",".gif"}.Contains(file.Extension.ToLowerInvariant()))throw new IOException("支持JPG、PNG、BMP、GIF静态图片，或H.264高清MP4视频。");
            EnsureAsset();Asset previous=asset;string id=Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(AssetFolder);
            string poster=Path.Combine(AssetFolder,id+".png"),movie=Path.Combine(AssetFolder,id+".mp4");bool committed=false;
            try
            {
                await Task.Run(delegate
                {
                    if(video)
                    {
                        File.Copy(full,movie,false);
                        using(var decoder=new MediaVideoDecoder(movie))
                        {long stamp;using(Bitmap first=decoder.ReadFrame(out stamp))
                        {if(first==null)throw new InvalidDataException("视频没有可读取的画面。");using(Bitmap cover=FitFrame(first,1920))cover.Save(poster,ImageFormat.Png);}}
                    }
                    else using(Image image=Image.FromFile(full))
                    {
                        if(image.Width>16384 || image.Height>16384 || (long)image.Width*image.Height>100000000)throw new InvalidDataException("图片尺寸过大，请缩小后导入。");
                        using(Bitmap cover=FitFrame(image,4096))cover.Save(poster,ImageFormat.Png);
                    }
                });
                await StopAndWait();
                AtomicText(Manifest,"v1\n"+(video?"video":"image")+"\n"+id);
                committed=true;loaded=false;asset=null;failed=false;LastError=null;
                paused=false;pauseLoaded=true;
                try{AtomicText(Path.Combine(PortablePaths.Data,"background-playback.txt"),"playing");}catch{/* The imported media is already committed; keep it usable for this run. */}
                DeleteManaged(previous);
            }
            finally
            {
                if(!committed){TryDelete(poster);TryDelete(movie);UpdatePlayback();}
            }
        }

        internal static async Task RestoreAsync()
        {
            EnsureAsset();Asset previous=asset;
            await StopAndWait();
            if(File.Exists(Manifest))File.Delete(Manifest);
            string legacy=Path.Combine(PortablePaths.Data,"custom-background.png");if(File.Exists(legacy))File.Delete(legacy);
            loaded=false;asset=null;failed=false;LastError=null;DeleteManaged(previous);
        }

        private static void DeleteManaged(Asset old)
        {
            if(old==null)return;Guid id;if(!Guid.TryParseExact(old.Id,"N",out id))return;
            TryDelete(Path.Combine(AssetFolder,id.ToString("N")+".png"));TryDelete(Path.Combine(AssetFolder,id.ToString("N")+".mp4"));
        }
        private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
        private static void AtomicText(string path,string text)
        {
            string temporary=path+".tmp";
            try{File.WriteAllText(temporary,text);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
            finally{TryDelete(temporary);}
        }
        internal static Bitmap FitFrame(Image image,int maximum)
        {
            double scale=Math.Min(1d,(double)maximum/Math.Max(image.Width,image.Height));
            var bitmap=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(bitmap)){g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(image,new Rectangle(Point.Empty,bitmap.Size));}
            return bitmap;
        }
        internal static void Shutdown()
        {
            shutdown=true;StopWorker();if(timer!=null){timer.Dispose();timer=null;}
        }
    }
}
