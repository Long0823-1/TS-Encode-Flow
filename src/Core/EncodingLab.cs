using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class PreviewPair {public string Original="",Filtered="";public double Seconds;}
 public static class EncodingLab {
  public static async Task<PreviewPair> Preview(string input,Preset p,Settings s,double seconds,string folder,CancellationToken token,Action<string> log) {
   Commands.Validate(p);var m=await MetadataReader.Read(input,s,p.ServiceId,token,log);if(seconds<0||seconds>=m.Duration)throw new ArgumentException("プレビュー時刻が範囲外です。");
   Directory.CreateDirectory(folder);string key=Guid.NewGuid().ToString("N");var pair=new PreviewPair {Original=Path.Combine(folder,key+"-source.png"),Filtered=Path.Combine(folder,key+"-filtered.png"),Seconds=seconds};
   AdvancedFilters.InputColor(p,m);var j=new Job {Input=input,Preset=p,Metadata=m};var part=new EncodePart {Start=seconds,End=Math.Min(m.Duration,seconds+12)};
   if(p.Cadence==CadenceMode.MixedVfr)part.Mode=await AdvancedEncoder.Cadence(j,s,part,token,log);else if(p.AutoInverseTelecine)await Telecine.Analyze(j,s,token,log);
   foreach(bool filtered in new[]{false,true}) {
    string vf=filtered?AdvancedFilters.Video(p,m,part.Mode):"null";var run=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-y","-ss",Commercials.N(AdvancedEncoder.SourceStart(m,seconds)),"-i",input,"-map","0:"+m.VideoIndex,"-vf",vf+",scale=trunc(iw*sar/2)*2:ih,setsar=1","-frames:v","1",filtered?pair.Filtered:pair.Original},token,null,120);
    if(run.ExitCode!=0)throw new InvalidDataException("比較画像を作成できません："+Converter.Tail(run.Error));
   }return pair;
  }
  public static async Task<List<QualityTrial>> Trial(string input,Preset preset,Settings s,IEnumerable<int> qualities,string folder,CancellationToken token,Action<string> log) {
   Commands.Validate(preset);var m=await MetadataReader.Read(input,s,preset.ServiceId,token,log);AdvancedFilters.InputColor(preset,m);
   var qs=qualities.Distinct().ToList();if(qs.Count<1||qs.Count>6||qs.Any(q=>q<1||q>51))throw new ArgumentException("品質値は1～51、最大6種類です。");
   Directory.CreateDirectory(folder);var results=new List<QualityTrial>();double sample=Math.Min(6,m.Duration/4);if(sample<1)throw new ArgumentException("試算には4秒以上の映像が必要です。");
   if(preset.AutoInverseTelecine&&preset.Cadence==CadenceMode.Preset)await Telecine.Analyze(new Job {Input=input,Preset=preset,Metadata=m},s,token,log);
   var modes=new Dictionary<double,string>();
   foreach(int q in qs) {
    var p=Store.Clone(preset);p.Quality=q;p.RateMode=RateMode.Quality;var item=new QualityTrial {Quality=q,Metric="SSIM（同じフィルタ適用後を基準）"};double weightedSsim=0;int metrics=0;
    foreach(double fraction in new[]{0.15,0.5,0.85}) {
     var j=new Job {Input=input,Preset=p,Metadata=Store.Clone(m)};var part=new EncodePart {Start=Math.Clamp(m.Duration*fraction-sample/2,0,m.Duration-sample),End=0};part.End=part.Start+sample;
     if(p.Cadence==CadenceMode.MixedVfr){if(!modes.TryGetValue(part.Start,out string mode)){mode=await AdvancedEncoder.Cadence(j,s,part,token,log);modes[part.Start]=mode;}part.Mode=mode;}
     string file=Path.Combine(folder,"q"+q+"-"+fraction.ToString("0.00",CultureInfo.InvariantCulture)+".mkv");var watch=Stopwatch.StartNew();await AdvancedEncoder.EncodeVideo(j,s,part,file,token,log);watch.Stop();await AdvancedEncoder.ValidateVideo(s,file,sample,token);
     item.EncodedSeconds+=watch.Elapsed.TotalSeconds;item.Seconds+=sample;item.Megabytes+=new FileInfo(file).Length/1048576.0;item.File=file;
     var rate=await Processes.Run(s.Ffprobe,new[]{"-v","error","-select_streams","v:0","-show_entries","stream=r_frame_rate","-of","default=nw=1:nk=1",file},token,null,30);
     double metricFps=MetadataReader.Ratio(rate.Output.Trim());if(rate.ExitCode!=0||metricFps<=0||!double.IsFinite(metricFps))throw new InvalidDataException("試算映像の比較FPSを取得できません。");
     // Matroska rounds timestamps to milliseconds. Align equal frame numbers
     // before framesync, otherwise it can compare a frame with its neighbour.
     string aligned="settb=AVTB,setpts=N/("+Commercials.N(metricFps)+"*TB)";
     string graph="[0:"+m.VideoIndex+"]"+AdvancedFilters.Video(p,j.Metadata,part.Mode)+","+aligned+"[ref];[1:v:0]"+aligned+"[dist];[ref][dist]ssim=shortest=1:repeatlast=0";
     var metric=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-ss",Commercials.N(AdvancedEncoder.SourceStart(m,part.Start)),"-i",input,"-i",file,"-t",Commercials.N(sample),"-filter_complex",graph,"-an","-f","null","-"},token,null,120);
     var match=Regex.Match(metric.Error,@"All:([0-9.]+)");if(metric.ExitCode==0&&match.Success){weightedSsim+=MetadataReader.Number(match.Groups[1].Value);metrics++;}else log("SSIM計測を省略："+Converter.Tail(metric.Error));
    }
    item.Speed=item.Seconds/Math.Max(0.001,item.EncodedSeconds);item.EstimatedMegabytes=item.Megabytes/item.Seconds*m.Duration+AdvancedOptions.Audio(p,m).Count*p.AudioBitrate*1000.0/8*m.Duration/1048576;
    item.Ssim=metrics==3?weightedSsim/metrics:-1;if(metrics!=3)item.Metric="SSIM測定不可（容量・速度のみ）";results.Add(item);log("試算 Q="+q+" 推定="+item.EstimatedMegabytes.ToString("F1",CultureInfo.InvariantCulture)+"MiB / "+item.Speed.ToString("F2",CultureInfo.InvariantCulture)+"倍速");
   }Store.Save(Path.Combine(folder,"trials.json"),results);return results;
  }
 }
}
