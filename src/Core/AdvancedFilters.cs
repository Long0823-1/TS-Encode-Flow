using System;
using System.Collections.Generic;
using System.Linq;

namespace TsEncodeFlow {
 public static class AdvancedFilters {
  public static string Matrix(VideoMatrix value)=>value switch {VideoMatrix.Bt709=>"bt709",VideoMatrix.Smpte170m=>"smpte170m",VideoMatrix.Bt2020nc=>"bt2020",_=>""};
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
