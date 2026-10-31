using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TsEncodeFlow {
 public sealed class Command {public string Exe;public List<string> Args=new List<string>();public override string ToString()=>Processes.Quote(Exe)+" "+string.Join(" ",Args.Select(Processes.Quote));}
 public static class Commands {
  static string N(int n)=>n.ToString(CultureInfo.InvariantCulture);
  public static void Validate(Preset p) {
   if(!Enum.IsDefined(p.CmMode)||!Enum.IsDefined(p.CmSource))throw new ArgumentException("CM出力設定が不正です。");
   if(p.CmMode==CmMode.Cut&&(p.CopyAudio||!string.IsNullOrWhiteSpace(p.Fps)))throw new ArgumentException("CMカット時は音声コピーをOFF、FPS指定を空欄にしてください。");
   if(!string.IsNullOrWhiteSpace(p.CmTrimFps)&&(MetadataReader.Ratio(p.CmTrimFps)<=0||MetadataReader.Ratio(p.CmTrimFps)>240||!double.IsFinite(MetadataReader.Ratio(p.CmTrimFps))))throw new ArgumentException("TrimのFPSが不正です。");
   if(p.Width<0||p.Height<0||p.Width>16384||p.Height>16384||p.Width%2!=0||p.Height%2!=0)throw new ArgumentException("幅・高さは0または2～16384の偶数で指定してください。");
   if(p.Quality<1||p.Quality>51||p.Bitrate<100||p.Bitrate>200000||p.AudioBitrate<32||p.AudioBitrate>640||p.Threads<0||p.Threads>128)throw new ArgumentException("品質・ビットレート・スレッド数が範囲外です。");
   if(new[]{p.CropLeft,p.CropTop,p.CropRight,p.CropBottom}.Any(n=>n<0||n%2!=0))throw new ArgumentException("クロップ量は0以上の偶数にしてください。");
   if(p.ServiceId<0||p.ServiceId>65535)throw new ArgumentException("サービスIDは0～65535です。");
   if(!string.IsNullOrWhiteSpace(p.Fps)&&(!Regex.IsMatch(p.Fps,@"^\d+(?:/\d+|\.\d+)?$")||MetadataReader.Ratio(p.Fps)<=0||MetadataReader.Ratio(p.Fps)>240))throw new ArgumentException("フレームレートは0より大きく240以下で指定してください。");
   foreach(string arg in Processes.Split(p.ExtraArgs??"")) {
    if(p.CmMode!=CmMode.Off&&new[]{"--trim","--seek","--seekto","--avsync","-ss","-t","-to","-vf","-af","-filter_complex","-filter_complex_script","-map"}.Contains(arg.Split('=')[0].ToLowerInvariant()))throw new ArgumentException("CM処理時に追加引数で時間・マッピングを変更できません。");
    string key=arg.Split('=')[0].ToLowerInvariant();
    if(new[]{"-i","-o","--input","--output","--output-format","-y","-n","-f","-report","-progress","--log","--log-file","--audio-file","--timecode","--chapter-copy","--chapter"}.Contains(key))throw new ArgumentException("追加引数で入出力やログの指定は変更できません："+arg);
   }
  }
  public static int[] Resolution(Preset p,Metadata m) {
   int w=m.Width-p.CropLeft-p.CropRight,h=m.Height-p.CropTop-p.CropBottom;if(w<2||h<2)throw new ArgumentException("クロップ量が映像サイズ以上です。");
   if(p.Width==0&&p.Height==0)return null;
   double displayW=w*m.Sar,displayH=h,ow=p.Width,oh=p.Height;
   if(p.KeepAspect) {
    double scale=Math.Min(p.Width==0?double.MaxValue:p.Width/displayW,p.Height==0?double.MaxValue:p.Height/displayH);
    if(!p.AllowUpscale)scale=Math.Min(1,scale);ow=displayW*scale;oh=displayH*scale;
   }else {
    if(ow==0)ow=displayW;if(oh==0)oh=displayH;
    if(!p.AllowUpscale){ow=Math.Min(ow,displayW);oh=Math.Min(oh,displayH);}
   }
   return new[]{Math.Max(2,(int)Math.Floor(ow/2)*2),Math.Max(2,(int)Math.Floor(oh/2)*2)};
  }
  public static string Filters(Preset p,Metadata m) {
   var f=new List<string>();int cropW=m.Width-p.CropLeft-p.CropRight,cropH=m.Height-p.CropTop-p.CropBottom;
   Resolution(p,m);
   if(p.CropLeft+p.CropTop+p.CropRight+p.CropBottom>0)f.Add("crop="+cropW+":"+cropH+":"+p.CropLeft+":"+p.CropTop);
   if(p.Deinterlace!=Deinterlace.None)f.Add((p.Deinterlace==Deinterlace.Yadif||p.Deinterlace==Deinterlace.YadifBob?"yadif":"bwdif")+"=mode="+(p.Deinterlace==Deinterlace.YadifBob||p.Deinterlace==Deinterlace.BwdifBob?"send_field":"send_frame")+":parity=auto:deint=all");
   if(p.Denoise)f.Add("hqdn3d=2:1.5:3:2.25");
   var r=Resolution(p,m);if(r!=null){f.Add("scale="+r[0]+":"+r[1]+":flags=lanczos");f.Add("setsar=1");}
   if(p.Sharpen)f.Add("unsharp=5:5:0.5");if(!string.IsNullOrWhiteSpace(p.Fps))f.Add("fps="+p.Fps);return string.Join(",",f);
  }
  public static Command Ffmpeg(Settings s,Preset p,Metadata m,string input,string output,bool lossless=false,bool remux=false,string cutFilter=null) {
   Validate(p);var c=new Command {Exe=s.Ffmpeg};var a=c.Args;
   a.AddRange(new[]{"-hide_banner","-nostdin","-y","-probesize","20000000","-analyzeduration","10000000","-i",input});
   if(cutFilter!=null){a.AddRange(new[]{"-/filter_complex",cutFilter,"-map","[vout]"});if(m.AudioIndex>=0)a.AddRange(new[]{"-map","[aout]"});}
   else {a.AddRange(new[]{"-map","0:"+m.VideoIndex});if(m.AudioIndex>=0)a.AddRange(new[]{"-map","0:"+m.AudioIndex});}
   if(remux)a.AddRange(new[]{"-c","copy"});
   else {
    string filters=Filters(p,m);if(filters.Length>0&&cutFilter==null)a.AddRange(new[]{"-vf",filters});
    string codec=lossless?"ffv1":p.Codec==Codec.H264?"libx264":p.Codec==Codec.HEVC?"libx265":"libsvtav1";a.AddRange(new[]{"-c:v",codec});
    if(lossless)a.AddRange(new[]{"-level","3","-g","1"});
    else {
     if(!string.IsNullOrWhiteSpace(p.Speed))a.AddRange(new[]{"-preset",p.Speed});
     if(p.RateMode==RateMode.Quality)a.AddRange(new[]{"-crf",N(p.Quality)});
     else {a.AddRange(new[]{"-b:v",p.Bitrate+"k"});if(p.RateMode==RateMode.CBR)a.AddRange(new[]{"-minrate",p.Bitrate+"k","-maxrate",p.Bitrate+"k","-bufsize",(p.Bitrate*2)+"k"});}
     a.AddRange(new[]{"-pix_fmt","yuv420p"});
    }
    if(p.Threads>0)a.AddRange(new[]{"-threads",N(p.Threads)});
    if(m.AudioIndex>=0) {a.AddRange(new[]{"-c:a",p.CopyAudio||lossless?"copy":"aac"});if(!p.CopyAudio&&!lossless)a.AddRange(new[]{"-b:a",p.AudioBitrate+"k"});}
   }
   a.AddRange(new[]{"-sn","-dn"});
   if(!remux) {
    a.AddRange(new[]{"-map_metadata","-1"});if(p.EmbedMetadata)a.AddRange(new[]{"-metadata","title="+m.Title,"-metadata","comment="+m.Channel});
    if(!lossless&&p.Container==Container.mp4){a.AddRange(new[]{"-movflags","+faststart"});if(p.Codec==Codec.HEVC)a.AddRange(new[]{"-tag:v","hvc1"});}
    if(!lossless)a.AddRange(Processes.Split(p.ExtraArgs??""));
   }
   a.AddRange(new[]{"-progress","pipe:1","-nostats",output});return c;
  }
  public static Command Hardware(Settings s,Preset p,Metadata m,string input,string output,bool prefiltered=false,List<TimeRange> keep=null) {
   Validate(p);var c=new Command {Exe=p.Engine==Engine.NVEncC?s.Nvenc:p.Engine==Engine.VCEEncC?s.Vceenc:s.Qsvenc};var a=c.Args;
   a.AddRange(new[]{"--avsw","-i",input,"-c",p.Codec==Codec.H264?"h264":p.Codec==Codec.HEVC?"hevc":"av1"});
   if(!string.IsNullOrWhiteSpace(p.Speed))a.AddRange(new[]{"--preset",p.Speed});
   if(p.RateMode==RateMode.Quality)a.AddRange(new[]{p.Engine==Engine.QSVEncC?"--icq":"--cqp",N(p.Quality)});else a.AddRange(new[]{p.RateMode==RateMode.CBR?"--cbr":"--vbr",N(p.Bitrate)});
   if(keep!=null)a.AddRange(new[]{"--trim",Commercials.HardwareTrim(keep,m.Fps)});
   if(!prefiltered) {
    if(p.CropLeft+p.CropTop+p.CropRight+p.CropBottom>0)a.AddRange(new[]{"--crop",string.Join(",",p.CropLeft,p.CropTop,p.CropRight,p.CropBottom)});
    if(p.Deinterlace==Deinterlace.Yadif||p.Deinterlace==Deinterlace.YadifBob)a.AddRange(new[]{"--vpp-yadif","mode="+(p.Deinterlace==Deinterlace.YadifBob?"bob":"auto")});
    if(p.Deinterlace==Deinterlace.Bwdif||p.Deinterlace==Deinterlace.BwdifBob)a.AddRange(new[]{"--vpp-bwdif","mode="+(p.Deinterlace==Deinterlace.BwdifBob?"bob":"frame")});
    var r=Resolution(p,m);if(r!=null)a.AddRange(new[]{"--output-res",r[0]+"x"+r[1],"--sar","1:1"});
    if(p.Denoise)a.Add("--vpp-knn");if(p.Sharpen)a.Add("--vpp-unsharp");
   }
   if(m.AudioIndex>=0&&keep==null){if(p.CopyAudio)a.AddRange(new[]{"--audio-copy","1"});else a.AddRange(new[]{"--audio-codec","1?aac","--audio-bitrate","1?"+p.AudioBitrate});}
   if(p.EmbedMetadata)a.AddRange(new[]{"--metadata","title="+m.Title});
   if(p.Container==Container.mp4&&p.Codec==Codec.HEVC)a.AddRange(new[]{"--video-tag","hvc1"});
   a.AddRange(Processes.Split(p.ExtraArgs??""));a.AddRange(new[]{"-o",output});return c;
  }
 }
}
