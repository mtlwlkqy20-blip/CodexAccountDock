using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexAccountDock
{
    // Media Foundation COM ABI: Microsoft WinSDK mfreadwrite.h/mfobjects.h.
    // https://learn.microsoft.com/windows/win32/medfound/processing-media-data-with-the-source-reader
    // No player control or audio renderer is created. The caller owns each frame.
    internal sealed class MediaVideoDecoder:IDisposable
    {
        private const uint Video=0xfffffffc,All=0xfffffffe;
        private static readonly Guid Processing=new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
        private static readonly Guid Major=new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid Subtype=new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid VideoType=new Guid("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid Rgb32=new Guid("00000016-0000-0010-8000-00aa00389b71");
        private static readonly Guid FrameSize=new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid StrideKey=new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static readonly Guid DurationKey=new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
        private static readonly Guid Buffer2D=new Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
        private IntPtr reader;
        private bool comStarted,mfStarted,disposed;
        private readonly int threadId;
        private int stride;
        private int codedWidth,codedHeight,cropX,cropY;
        private byte[] row;
        internal int Width {get;private set;}
        internal int Height {get;private set;}
        internal long DurationTicks {get;private set;}

        internal MediaVideoDecoder(string localPath)
        {
            threadId=Thread.CurrentThread.ManagedThreadId;
            if(string.IsNullOrWhiteSpace(localPath) || !Path.IsPathRooted(localPath) || localPath.StartsWith(@"\\",StringComparison.Ordinal) || !File.Exists(localPath))
                throw new IOException("请选择本机磁盘上的MP4视频。");
            IntPtr attributes=IntPtr.Zero,type=IntPtr.Zero;
            try
            {
                Check(CoInitializeEx(IntPtr.Zero,0));comStarted=true;
                Check(MFStartup(0x20070,0));mfStarted=true;
                Check(MFCreateAttributes(out attributes,1));
                Guid processing=Processing;Check(Call<SetU32>(attributes,21)(attributes,ref processing,1));
                Check(MFCreateSourceReaderFromURL(Path.GetFullPath(localPath),attributes,out reader));
                Check(Call<SelectStream>(reader,4)(reader,All,false));
                Check(Call<SelectStream>(reader,4)(reader,Video,true));
                Check(MFCreateMediaType(out type));
                SetGuid(type,Major,VideoType);SetGuid(type,Subtype,Rgb32);
                Check(Call<SetType>(reader,7)(reader,Video,IntPtr.Zero,type));
                ReadFormat();
                PropVariant duration=new PropVariant();Guid key=DurationKey;
                try{int hr=Call<GetAttribute>(reader,12)(reader,0xffffffff,ref key,out duration);if(hr>=0 && (duration.Kind==21 || duration.Kind==20))DurationTicks=duration.Value;}
                finally{PropVariantClear(ref duration);}
            }
            catch(Exception ex)
            {
                Dispose();
                if(ex is InvalidDataException || ex is IOException)throw;
                throw new InvalidDataException("系统无法解码此视频，请使用H.264编码的MP4。Windows N版本需启用媒体功能。",ex);
            }
            finally{Release(ref type);Release(ref attributes);}
        }

        private void ReadFormat()
        {
            IntPtr type=IntPtr.Zero;
            try
            {
                Check(Call<GetMediaType>(reader,6)(reader,Video,out type));
                ulong size;Guid key=FrameSize;Check(Call<GetU64>(type,8)(type,ref key,out size));
                uint width=(uint)(size>>32),height=(uint)size;
                if(width==0 || height==0 || width>4096 || height>4096 || (ulong)width*height>8500000)throw new InvalidDataException("背景视频最高支持4K，建议使用1080p视频。");
                codedWidth=Width=(int)width;codedHeight=Height=(int)height;cropX=cropY=0;
                foreach(string aperture in new[]{"d7388766-18fe-48c6-a177-ee894867c8c4","66758743-7e5f-400d-980a-aa8596c85696"})
                {
                    byte[] area=new byte[16];uint sizeOut;Guid areaKey=new Guid(aperture);
                    if(Call<GetBlob>(type,15)(type,ref areaKey,area,16,out sizeOut)>=0 && sizeOut==16)
                    {
                        int x=BitConverter.ToInt16(area,2),y=BitConverter.ToInt16(area,6),w=BitConverter.ToInt32(area,8),h=BitConverter.ToInt32(area,12);
                        if(x>=0 && y>=0 && w>0 && h>0 && x+w<=codedWidth && y+h<=codedHeight){cropX=x;cropY=y;Width=w;Height=h;break;}
                    }
                }
                uint raw;key=StrideKey;
                stride=Call<GetU32>(type,7)(type,ref key,out raw)>=0?unchecked((int)raw):-codedWidth*4;
                if(Math.Abs((long)stride)<codedWidth*4L || Math.Abs((long)stride)>codedWidth*4L+65536)throw new InvalidDataException("视频像素格式无效。");
                row=new byte[Width*4];
            }
            finally{Release(ref type);}
        }

        internal Bitmap ReadFrame(out long timestampTicks)
        {
            CheckThread();timestampTicks=0;
            for(int skipped=0;skipped<100;skipped++)
            {
                IntPtr sample=IntPtr.Zero,buffer=IntPtr.Zero;
                try
                {
                    uint actual,flags;long stamp;
                    Check(Call<ReadSample>(reader,9)(reader,Video,0,out actual,out flags,out stamp,out sample));
                    if((flags&1)!=0)throw new InvalidDataException("视频解码失败。");
                    if((flags&0x20)!=0)ReadFormat();
                    if(sample==IntPtr.Zero){if((flags&2)!=0)return null;continue;}
                    Check(Call<GetBuffer>(sample,41)(sample,out buffer));
                    timestampTicks=stamp;
                    return CopyBuffer(buffer);
                }
                finally{Release(ref buffer);Release(ref sample);}
            }
            throw new InvalidDataException("视频没有可播放的画面。");
        }

        private Bitmap CopyBuffer(IntPtr buffer)
        {
            IntPtr two=IntPtr.Zero,data=IntPtr.Zero;bool locked=false;bool is2D=false;int pitch=stride;
            Bitmap bitmap=null;
            try
            {
                Guid iid=Buffer2D;
                if(Marshal.QueryInterface(buffer,ref iid,out two)>=0 && two!=IntPtr.Zero)
                {
                    if(Call<Lock2D>(two,3)(two,out data,out pitch)>=0){is2D=true;locked=true;}
                }
                if(!locked)
                {
                    uint maximum,length;Check(Call<LockBuffer>(buffer,3)(buffer,out data,out maximum,out length));locked=true;
                    long required=Math.Abs((long)pitch)*(codedHeight-1)+codedWidth*4L;
                    if(required>length)throw new InvalidDataException("视频帧数据不完整。");
                    if(pitch<0)data=IntPtr.Add(data,checked((int)(-(long)pitch*(codedHeight-1))));
                }
                if(data==IntPtr.Zero || Math.Abs((long)pitch)<codedWidth*4L || Math.Abs((long)pitch)>codedWidth*4L+65536)throw new InvalidDataException("视频行跨度无效。");
                data=IntPtr.Add(data,checked(cropY*pitch+cropX*4));
                bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppRgb);
                BitmapData target=bitmap.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
                try
                {
                    for(int y=0;y<Height;y++)
                    {
                        Marshal.Copy(IntPtr.Add(data,checked(y*pitch)),row,0,row.Length);
                        Marshal.Copy(row,0,IntPtr.Add(target.Scan0,y*target.Stride),row.Length);
                    }
                }
                finally{bitmap.UnlockBits(target);}
                Bitmap result=bitmap;bitmap=null;return result;
            }
            finally
            {
                if(bitmap!=null)bitmap.Dispose();
                if(locked){if(is2D)Call<Unlock>(two,4)(two);else Call<Unlock>(buffer,4)(buffer);}
                Release(ref two);
            }
        }

        internal void Rewind()
        {
            CheckThread();Guid empty=Guid.Empty;var position=new PropVariant {Kind=20,Value=0};
            Check(Call<SetPosition>(reader,8)(reader,ref empty,ref position));
        }

        private void CheckThread()
        {
            if(disposed)throw new ObjectDisposedException("MediaVideoDecoder");
            if(Thread.CurrentThread.ManagedThreadId!=threadId)throw new InvalidOperationException("视频解码器必须在创建线程释放和使用。");
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            Release(ref reader);row=null;
            if(mfStarted){MFShutdown();mfStarted=false;}
            if(comStarted){CoUninitialize();comStarted=false;}
        }
        private static void Release(ref IntPtr value){if(value!=IntPtr.Zero){Marshal.Release(value);value=IntPtr.Zero;}}
        private static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
        private static T Call<T>(IntPtr instance,int slot) where T:class
        {return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T)) as T;}
        private static void SetGuid(IntPtr type,Guid key,Guid value){Check(Call<SetGuidValue>(type,24)(type,ref key,ref value));}

        [StructLayout(LayoutKind.Explicit,Size=24)]
        private struct PropVariant {[FieldOffset(0)]internal ushort Kind;[FieldOffset(8)]internal long Value;}
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int SetU32(IntPtr self,ref Guid key,uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetU32(IntPtr self,ref Guid key,out uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetU64(IntPtr self,ref Guid key,out ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetBlob(IntPtr self,ref Guid key,[Out]byte[] bytes,uint size,out uint actual);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int SetGuidValue(IntPtr self,ref Guid key,ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int SelectStream(IntPtr self,uint stream,[MarshalAs(UnmanagedType.Bool)]bool selected);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int SetType(IntPtr self,uint stream,IntPtr reserved,IntPtr mediaType);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetMediaType(IntPtr self,uint stream,out IntPtr mediaType);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int SetPosition(IntPtr self,ref Guid format,ref PropVariant position);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int ReadSample(IntPtr self,uint stream,uint flags,out uint actual,out uint resultFlags,out long timestamp,out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetAttribute(IntPtr self,uint stream,ref Guid key,out PropVariant value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetBuffer(IntPtr self,out IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int LockBuffer(IntPtr self,out IntPtr data,out uint maximum,out uint length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Lock2D(IntPtr self,out IntPtr firstRow,out int pitch);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Unlock(IntPtr self);
        [DllImport("ole32.dll")]private static extern int CoInitializeEx(IntPtr reserved,uint flags);
        [DllImport("ole32.dll")]private static extern void CoUninitialize();
        [DllImport("ole32.dll")]private static extern int PropVariantClear(ref PropVariant value);
        [DllImport("mfplat.dll",ExactSpelling=true)]private static extern int MFStartup(int version,int flags);
        [DllImport("mfplat.dll",ExactSpelling=true)]private static extern int MFShutdown();
        [DllImport("mfplat.dll",ExactSpelling=true)]private static extern int MFCreateAttributes(out IntPtr attributes,uint size);
        [DllImport("mfplat.dll",ExactSpelling=true)]private static extern int MFCreateMediaType(out IntPtr mediaType);
        [DllImport("mfreadwrite.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int MFCreateSourceReaderFromURL(string url,IntPtr attributes,out IntPtr reader);
    }
}
