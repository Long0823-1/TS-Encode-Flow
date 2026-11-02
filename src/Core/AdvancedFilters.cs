using System;
using System.Collections.Generic;
using System.Linq;

namespace TsEncodeFlow {
 public static class AdvancedFilters {
  public static string Matrix(VideoMatrix value)=>value switch {VideoMatrix.Bt709=>"bt709",VideoMatrix.Smpte170m=>"smpte170m",VideoMatrix.Bt2020nc=>"bt2020",_=>""};
  public static double OutputFps(Preset p,Metadata m,string mode="preset") {
   if(!string.IsNullOrWhiteSpace(p.Fps)&&p.Cadence!=CadenceMode.MixedVfr)return MetadataReader.Ratio(p.Fps);
   if(mode=="film"||mode=="preset"&&m.InverseTelecine)return 24000.0/1001;
   return m.Fps*(mode=="video"||mode=="preset"&&(p.Deinterlace==Deinterlace.YadifBob||p.Deinterlace==Deinterlace.BwdifBob)?2:1);
  }
  public static string Rate(double fps) {
   foreach(var ratio in new[]{"24000/1001","30000/1001","60000/1001","120000/1001"})if(Math.Abs(MetadataReader.Ratio(ratio)-fps)<0.00001)return ratio;
   int numerator=(int)Math.Round(fps*10000),denominator=10000,a=numerator,b=denominator;while(b!=0){int rest=a%b;a=b;b=rest;}return (numerator/Math.Max(1,a))+"/"+(denominator/Math.Max(1,a));
  }
  public static string Timeline(Preset p,Metadata m,string mode="preset") {
   double fps=OutputFps(p,m,mode);if(!double.IsFinite(fps)||fps<=0)throw new ArgumentException("映像区間の出力FPSを取得できません。");
   // Y4M has no per-frame timestamps. Fill holes on the source clock before
   // sending it to the hardware encoder, rather than shortening the video.
   // Input seeking already gives timestamps relative to the requested start.
   // STARTPTS would erase a delayed first decodable frame after a damaged GOP.
   return Video(p,m,mode)+",fps=fps="+Rate(fps)+":start_time=0:round=near";
  }
  public static string Video(Preset preset,Metadata metadata,string mode="preset") {
   var p=Store.Clone(preset);var m=Store.Clone(metadata);p.Fps="";m.InverseTelecine=mode=="film"||mode=="preset"&&metadata.InverseTelecine;
   if(mode=="video")p.Deinterlace=Deinterlace.BwdifBob;
   if(mode=="progressive")p.Deinterlace=Deinterlace.None;
   var filters=new List<string>();
   if(p.InputColorMatrix!=VideoMatrix.Preserve)filters.Add("setparams=colorspace="+m.ColorSpace+":color_primaries="+m.ColorPrimaries+":color_trc="+m.ColorTransfer);
   filters.Add(Commands.Filters(p,m));
   if(p.ColorMatrix!=VideoMatrix.Preserve) {
    if(string.IsNullOrWhiteSpace(m.ColorSpace)||m.ColorSpace=="unknown"||string.IsNullOrWhiteSpace(m.ColorPrimaries)||m.ColorPrimaries=="unknown"||string.IsNullOrWhiteSpace(m.ColorTransfer)||m.ColorTransfer=="unknown")throw new ArgumentException("色域変換には入力の色メタデータが必要です。入力色設定を手動指定してください。");
    filters.Add("colorspace=all="+Matrix(p.ColorMatrix)+":ispace="+m.ColorSpace+":iprimaries="+m.ColorPrimaries+":itrc="+m.ColorTransfer+":format="+(p.BitDepth==10?"yuv420p10":"yuv420p")+":range="+(p.ColorRange==VideoRange.Full?"pc":p.ColorRange==VideoRange.Limited?"tv":m.ColorRange=="pc"?"pc":"tv")+":dither="+(p.Dither==DitherMode.None?"none":"fsb"));
   }else {
    string scale="scale=iw:ih:flags=accurate_rnd:sws_dither="+(p.Dither==DitherMode.Bayer?"bayer":p.Dither==DitherMode.None?"none":"ed");
    if(p.ColorRange!=VideoRange.Preserve)scale+=":in_range=auto:out_range="+(p.ColorRange==VideoRange.Full?"full":"limited");
    filters.Add(scale);filters.Add("format="+(p.BitDepth==10?"yuv420p10le":"yuv420p"));
    if(p.ColorRange!=VideoRange.Preserve)filters.Add("setparams=range="+(p.ColorRange==VideoRange.Full?"full":"limited"));
   }
   if(!string.IsNullOrWhiteSpace(preset.Fps)&&preset.Cadence!=CadenceMode.MixedVfr)filters.Add("fps="+preset.Fps);
   return string.Join(",",filters.Where(x=>x.Length>0));
  }
  public static void InputColor(Preset p,Metadata m) {
   if(p.InputColorMatrix==VideoMatrix.Preserve)return;
   m.ColorSpace=p.InputColorMatrix==VideoMatrix.Bt2020nc?"bt2020nc":Matrix(p.InputColorMatrix);
   m.ColorPrimaries=p.InputColorMatrix==VideoMatrix.Bt2020nc?"bt2020":Matrix(p.InputColorMatrix);
   m.ColorTransfer=p.InputColorMatrix==VideoMatrix.Bt2020nc?"bt2020-10":p.InputColorMatrix==VideoMatrix.Smpte170m?"smpte170m":"bt709";
  }
 }
}
