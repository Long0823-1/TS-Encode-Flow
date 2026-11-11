using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 // chapter_exe seeks back and forth through audio. Damaged AAC can make the
 // L-SMASH audio reader spin indefinitely. Decode only audio once, bounded to
 // the already verified video timeline; video is still read directly from TS.
 public static class CmAudio {
  public static string Filter(Metadata m) {
   string N(double n)=>n.ToString("R",CultureInfo.InvariantCulture);
   double offset=m.VideoStart-m.AudioStart;
   string align=offset>=0?"atrim=start="+N(offset)+",asetpts=PTS-STARTPTS":"adelay="+N(-offset*1000)+":all=1";
   return "asetpts=PTS-STARTPTS,"+align+",apad=whole_dur="+N(m.Duration)+",atrim=duration="+N(m.Duration);
  }
  public static async Task Prepare(Job job,Settings settings,string wav,CancellationToken token,Action<string> log) {
   var m=job.Metadata;
   StorageSpace.Require(wav);
   if(m.AudioIndex<0||!double.IsFinite(m.Duration)||m.Duration<=0||!double.IsFinite(m.VideoStart)||!double.IsFinite(m.AudioStart))throw new InvalidDataException("CM解析用音声の時間軸が不正です。");
   var result=await Processes.Run(settings.Ffmpeg,new[]{"-hide_banner","-nostdin","-v","warning","-y","-threads","4","-i",job.MediaInput,"-map","0:"+m.AudioIndex,"-vn","-af",Filter(m),"-ar","48000","-ac","2","-c:a","pcm_s16le",wav},token,log,timeoutSeconds:Math.Max(120,(int)Math.Ceiling(m.Duration)));
   if(result.ExitCode!=0||!File.Exists(wav)||new FileInfo(wav).Length<44)throw new InvalidDataException("CM解析用の音声を準備できません："+Converter.Tail(result.Error));
  }
 }
}
