using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class TelecineSample {
  public double Start,Seconds,Cadence;
  public int Frames,Top,Bottom;
  public bool Film;
  public int MatchFrames,CombedFrames,RepairedFrames,Cycles,DuplicateCycles,DetectedTop,DetectedBottom,DetectedProgressive;
  public string Method="";
  public bool Contradiction;
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
   if(j.Preset.Cadence==CadenceMode.MixedVfr){result.Reason="区間ごとの混在VFR解析を使用";return result;}
   if(!j.Preset.AutoInverseTelecine){result.Reason="自動逆テレシネOFF";return result;}
   if(Math.Abs(j.Metadata.Fps-30000.0/1001)>0.015){result.Reason="29.97fps以外：元のフィルタを使用";return result;}
   var windows=Windows(j.Metadata,Commercials.Keep(j));
   if(windows.Count<3){result.Reason="判定に必要な独立区間が不足：元のフィルタを使用";return result;}
   foreach(var w in windows) {
    var sample=await Probe(j,settings,w.Start,w.End-w.Start,token,log);result.Samples.Add(sample);
   }
   int film=result.Samples.Count(s=>s.Film),conflicts=result.Samples.Count(s=>s.Contradiction);
   result.Applied=film>=2&&conflicts==0;
   result.Reason=result.Applied?"複数区間で24p復元を検証：fieldmatch＋残留縞解除＋decimateで23.976fps":film>0?"24p区間を"+film+"か所検出。ただし30p・60i等の混在があるため全編23.976fps固定を見送ります。「区間別24/30/60fps・VFR」で処理できます。":"24p復元の証拠が不足：設定済みのインターレース解除を使用";
   j.Metadata.InverseTelecine=result.Applied;log("逆テレシネ判定："+result.Reason);return result;
  }
  public static bool MatchFilm(TelecineSample s)=>s.MatchFrames>=120&&s.Cycles>=20&&s.RepairedFrames>=8&&(double)s.RepairedFrames/s.MatchFrames>=0.04&&(double)s.CombedFrames/s.MatchFrames<=0.03&&(double)s.DuplicateCycles/s.Cycles>=0.90;
  public static void MatchLine(TelecineSample sample,string line) {
   var m=Regex.Match(line,@"SC:[01]\s*\|\s*COMBS:\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+).*?combed=(YES|NO)");
   if(m.Success){sample.MatchFrames++;if(m.Groups[6].Value=="YES")sample.CombedFrames++;int current=int.Parse(m.Groups[2].Value);int best=Enumerable.Range(1,5).Min(i=>int.Parse(m.Groups[i].Value));if(current>80&&best<20)sample.RepairedFrames++;}
   if(line.Contains("1/5 frame drop:"))sample.Cycles++;
   if(Regex.IsMatch(line,@"#\d+:.*\sdup(?:\s|$)"))sample.DuplicateCycles++;
  }
  public static async Task<TelecineSample> Probe(Job j,Settings settings,double start,double seconds,CancellationToken token,Action<string> log,bool verifyAll=true) {
   token.ThrowIfCancellationRequested();var sequence=new List<string>();int tff=0,bff=0,progressive=0;
   var args=new[]{"-hide_banner","-nostdin","-ss",Commercials.N(AdvancedEncoder.SourceStart(j.Metadata,start)),"-i",j.Input,"-map","0:"+j.Metadata.VideoIndex,"-t",Commercials.N(seconds),"-vf","idet,metadata=mode=print","-an","-sn","-dn","-f","null","-"};
   var run=await Processes.Run(settings.Ffmpeg,args,token,line=>{const string key="lavfi.idet.repeated.current_frame=";int at=line.IndexOf(key,StringComparison.Ordinal);if(at>=0)sequence.Add(line.Substring(at+key.Length).Trim());const string field="lavfi.idet.multiple.current_frame=";at=line.IndexOf(field,StringComparison.Ordinal);if(at>=0){string value=line.Substring(at+field.Length).Trim();if(value=="tff")tff++;else if(value=="bff")bff++;else if(value=="progressive")progressive++;}},120);
   if(run.ExitCode!=0)throw new System.IO.InvalidDataException("逆テレシネ解析に失敗しました："+Converter.Tail(run.Error));
   var sample=Classify(sequence);sample.Start=start;sample.Seconds=seconds;sample.DetectedTop=tff;sample.DetectedBottom=bff;sample.DetectedProgressive=progressive;sample.Method=sample.Film?"3:2フィールド周期":"証拠不足";
   if(!sample.Film&&seconds>=4&&(verifyAll||sample.Top>=4&&sample.Bottom>=4)) {
    var match=await Processes.Run(settings.Ffmpeg,new[]{"-hide_banner","-nostdin","-loglevel","debug","-ss",Commercials.N(AdvancedEncoder.SourceStart(j.Metadata,start)),"-i",j.Input,"-map","0:"+j.Metadata.VideoIndex,"-t",Commercials.N(Math.Min(8,seconds)),"-vf","fieldmatch=order=auto:combmatch=full:combdbg=pcnub,decimate=cycle=5","-an","-sn","-dn","-f","null","-"},token,line=>MatchLine(sample,line),180);
    if(match.ExitCode!=0)throw new System.IO.InvalidDataException("フィールドマッチ補助解析に失敗しました："+Converter.Tail(match.Error));
    sample.Film=MatchFilm(sample);sample.Contradiction=sample.MatchFrames>=120&&((double)sample.CombedFrames/sample.MatchFrames>0.15||sample.Cycles>=20&&(double)sample.DuplicateCycles/sample.Cycles<0.65);
    sample.Method=sample.Film?"フィールドマッチで縞修復＋5枚周期の重複を検証":sample.Contradiction?"24p固定と両立しない動きを検出":"静止・低動作等で判定保留";
   }
   log("逆テレシネ解析："+Commercials.N(start)+"秒 / "+sample.Method+" / 3:2一致率 "+sample.Cadence.ToString("P1",System.Globalization.CultureInfo.InvariantCulture)+" / 修復 "+sample.RepairedFrames+"・残留縞 "+sample.CombedFrames+"/"+sample.MatchFrames+"・重複周期 "+sample.DuplicateCycles+"/"+sample.Cycles);
   return sample;
  }
 }
}
