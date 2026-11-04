using System;
using System.Collections.Generic;
using System.Linq;

namespace TsEncodeFlow {
 public sealed class EpgCandidate {
  public int EventId;
  public string Title="",Description="";
  public DateTime Start;
  public double Duration;
 }
 public static class EpgSelection {
  public static double Overlap(EpgCandidate item,DateTime? recordingStart,double duration) {
   if(item==null||!recordingStart.HasValue||!double.IsFinite(duration)||duration<=0||duration>=7*86400||item.Start==default||!double.IsFinite(item.Duration)||item.Duration<=0||item.Duration>=7*86400)return 0;
   if(recordingStart.Value>DateTime.MaxValue.AddSeconds(-duration)||item.Start>DateTime.MaxValue.AddSeconds(-item.Duration))return 0;
   return Math.Max(0,(new[]{recordingStart.Value.AddSeconds(duration),item.Start.AddSeconds(item.Duration)}.Min()-new[]{recordingStart.Value,item.Start}.Max()).TotalSeconds);
  }
  public static EpgCandidate Choose(IEnumerable<EpgCandidate> candidates,DateTime? recordingStart,double duration,EpgCandidate middle,EpgCandidate beginning) {
   var all=candidates.Where(x=>!string.IsNullOrWhiteSpace(x.Title)).ToList();
   if(recordingStart.HasValue&&double.IsFinite(duration)&&duration>0&&duration<7*86400) {
    var selected=all.Where(x=>x.Start!=default&&double.IsFinite(x.Duration)&&x.Duration>0&&x.Duration<7*86400)
     .Select(x=>new {Event=x,Overlap=Overlap(x,recordingStart,duration)})
     .Where(x=>x.Overlap>0).OrderByDescending(x=>x.Overlap).ThenByDescending(x=>middle!=null&&x.Event.Start==middle.Start&&x.Event.EventId==middle.EventId).ThenBy(x=>x.Event.Start).FirstOrDefault();
    if(selected!=null)return selected.Event;
   }
   return !string.IsNullOrWhiteSpace(middle?.Title)?middle:!string.IsNullOrWhiteSpace(beginning?.Title)?beginning:all.FirstOrDefault();
  }
 }
 // EIT and TDT/TOT carry broadcast wall-clock values in the same time domain.
 // Anchor the first broadcast clock to the selected service's PCR, never the
 // file modification time or a fixed pre-recording margin.
 public sealed class EpgClock {
  readonly int pid;double? first,last;double wrap;const double Period=(1L<<33)/90000.0;
  public DateTime? RecordingStart {get;private set;}
  public EpgClock(int pcrPid){pid=pcrPid;}
  public void Add(byte[] p) {
   if(p.Length!=188||p[0]!=0x47||(p[1]&128)!=0||(p[3]&192)!=0)return;
   int id=((p[1]&31)<<8)|p[2],control=(p[3]>>4)&3;
   if(id==pid&&(control==2||control==3)&&p[4]>=7&&p[4]<=183&&(p[5]&16)!=0) {
    double value=((long)p[6]<<25|((long)p[7]<<17)|((long)p[8]<<9)|((long)p[9]<<1)|(long)(p[10]>>7))/90000.0;
    if(last.HasValue&&value+wrap<last.Value-Period/2)wrap+=Period;
    value+=wrap;if(!first.HasValue)first=value;last=value;
   }
   if(RecordingStart.HasValue||!first.HasValue||!last.HasValue||id!=0x14||(p[1]&64)==0||(control!=1&&control!=3))return;
   int at=control==3?5+p[4]:4;if(at>=188)return;at+=1+p[at];
   if(at+8>188||(p[at]!=0x70&&p[at]!=0x73)||(((p[at+1]&15)<<8)|p[at+2])<5)return;
   int Bcd(byte v)=>((v&15)>9||(v>>4)>9)?-1:(v>>4)*10+(v&15);
   int h=Bcd(p[at+5]),m=Bcd(p[at+6]),s=Bcd(p[at+7]);if(h<0||h>23||m<0||m>59||s<0||s>59)return;
   int mjd=p[at+3]*256+p[at+4];
   var clock=new DateTime(1858,11,17).AddDays(mjd).AddHours(h).AddMinutes(m).AddSeconds(s);
   double elapsed=last.Value-first.Value;if(elapsed<0||elapsed>86400)return;
   RecordingStart=clock.AddSeconds(-elapsed);
  }
 }
}
