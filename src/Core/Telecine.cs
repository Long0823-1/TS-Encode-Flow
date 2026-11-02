using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class TelecineSample {
  public double Start,Seconds,Cadence;
  public int Frames,Top,Bottom;
  public bool Film;
 }
 public sealed class TelecineResult {
  public bool Applied;
  public string Reason="";
  public List<TelecineSample> Samples=new();
 }
 public static class Telecine {
  // A real 3:2 sequence repeats each field once per five frames. Static scenes
  // and the repeated frames of 30p animation are not evidence on their own.
  public static TelecineSample Classify(IEnumerable<string> sequence) {
   var a=sequence.ToArray();int good=0,blocks=0;
   for(int i=5;i+5<=a.Length;i+=5){var b=a.Skip(i).Take(5).ToArray();blocks++;if(b.Count(x=>x=="top")==1&&b.Count(x=>x=="bottom")==1&&b.Count(x=>x=="neither")==3)good++;}
   var s=new TelecineSample {Frames=a.Length,Top=a.Count(x=>x=="top"),Bottom=a.Count(x=>x=="bottom"),Cadence=blocks==0?0:(double)good/blocks};
   s.Film=s.Frames>=120&&s.Cadence>=0.90&&(double)s.Top/s.Frames>=0.16&&(double)s.Bottom/s.Frames>=0.16;
   return s;
  }
  public static List<TimeRange> Windows(Metadata m,List<TimeRange> keep) {
   var ranges=(keep??new List<TimeRange>{new(){Start=0,End=m.Duration}}).Where(r=>r.End-r.Start>=6).ToList();
   double total=ranges.Sum(r=>r.End-r.Start);var result=new List<TimeRange>();
   if(total<24)return result;
   foreach(double fraction in new[]{0.1,0.3,0.5,0.7,0.9}) {
    double point=total*fraction;foreach(var r in ranges){double len=r.End-r.Start;if(point>len){point-=len;continue;}double seconds=Math.Min(8,len),start=Math.Clamp(r.Start+point-seconds/2,r.Start,r.End-seconds);if(!result.Any(x=>Math.Abs(x.Start-start)<seconds))result.Add(new(){Start=start,End=start+seconds});break;}
   }return result;
  }
  public static async Task<TelecineResult> Analyze(Job j,Settings settings,CancellationToken token,Action<string> log) {
   var result=new TelecineResult();j.Metadata.InverseTelecine=false;
   if(!j.Preset.AutoInverseTelecine){result.Reason="自動逆テレシネOFF";return result;}
   if(Math.Abs(j.Metadata.Fps-30000.0/1001)>0.015){result.Reason="29.97fps以外：元のフィルタを使用";return result;}
   var windows=Windows(j.Metadata,Commercials.Keep(j));
   if(windows.Count<3){result.Reason="判定に必要な独立区間が不足：元のフィルタを使用";return result;}
   foreach(var w in windows) {
    token.ThrowIfCancellationRequested();var sequence=new List<string>();
    var args=new[]{"-hide_banner","-nostdin","-ss",Commercials.N(w.Start),"-i",j.Input,"-map","0:"+j.Metadata.VideoIndex,"-t",Commercials.N(w.End-w.Start),"-vf","idet,metadata=mode=print:key=lavfi.idet.repeated.current_frame","-an","-sn","-dn","-f","null","-"};
    var run=await Processes.Run(settings.Ffmpeg,args,token,line=>{const string key="lavfi.idet.repeated.current_frame=";int at=line.IndexOf(key,StringComparison.Ordinal);if(at>=0)sequence.Add(line.Substring(at+key.Length).Trim());},120);
    if(run.ExitCode!=0)throw new System.IO.InvalidDataException("逆テレシネ解析に失敗しました："+Converter.Tail(run.Error));
    var sample=Classify(sequence);sample.Start=w.Start;sample.Seconds=w.End-w.Start;result.Samples.Add(sample);
    log("逆テレシネ解析："+Commercials.N(w.Start)+"秒 / "+sample.Frames+" frames / 3:2一致率 "+sample.Cadence.ToString("P1",System.Globalization.CultureInfo.InvariantCulture));
   }
   result.Applied=result.Samples.All(s=>s.Film);
   result.Reason=result.Applied?"全サンプルで3:2を検出：fieldmatch＋残留縞解除＋decimateで23.976fps":"3:2の確証なし／混在の可能性：元のフィルタを使用";
   j.Metadata.InverseTelecine=result.Applied;log("逆テレシネ判定："+result.Reason);return result;
  }
 }
}
