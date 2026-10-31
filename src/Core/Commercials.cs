using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public enum CmMode { Off,Chapters,Cut }
 public enum CmSource { Sidecar,Comskip,Amatsukaze }
 public sealed class TimeRange {public double Start,End;public bool IsCommercial;}
 public sealed class CmPlan {
  public string Fingerprint="",Source="",ChapterFile="";
  public bool Approved,Reviewed,HasVideoStart;
  public double SourceVideoStart;
  public double Duration,Fps;
  public List<TimeRange> Segments=new();
  public List<TimeRange> Keep=>Segments.Where(x=>!x.IsCommercial).ToList();
  public double OutputDuration=>Keep.Sum(x=>x.End-x.Start);
 }
 public sealed class CmReviewException:Exception {public CmReviewException(string message):base(message){}}
 public static class Commercials {
  static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
  public static string N(double n)=>n.ToString("0.#########",Inv);
  public static string Time(double n)=>((long)(n/3600)).ToString("00",Inv)+":"+TimeSpan.FromSeconds(n).ToString(@"mm\:ss\.fff",Inv);
  public static double ParseTime(string text) {
   if(double.TryParse(text,NumberStyles.Float,Inv,out double seconds)&&double.IsFinite(seconds)&&seconds>=0)return seconds;
   var clock=Regex.Match(text,@"^(\d+):([0-5]\d):([0-5]\d(?:\.\d+)?)$");if(clock.Success)return double.Parse(clock.Groups[1].Value,Inv)*3600+double.Parse(clock.Groups[2].Value,Inv)*60+double.Parse(clock.Groups[3].Value,Inv);
   if(TimeSpan.TryParse(text,Inv,out var time)&&time.TotalSeconds>=0)return time.TotalSeconds;
   throw new ArgumentException("時刻を秒数または hh:mm:ss.fff で入力してください。");
  }
  public static CmPlan Build(IEnumerable<TimeRange> commercials,double duration,double fps,string fingerprint,string source="手動") {
   if(!double.IsFinite(duration)||duration<=0||!double.IsFinite(fps)||fps<=0||fps>240)throw new ArgumentException("CM解析に有効な録画時間・FPSが必要です。");
   var ranges=commercials.ToList();if(ranges.Count>256)throw new ArgumentException("CM区間は256個以下にしてください。");
   var normalized=new List<TimeRange>();
   foreach(var r in ranges.OrderBy(x=>x.Start)) {
    if(!double.IsFinite(r.Start)||!double.IsFinite(r.End)||r.Start<0||r.End<=r.Start||r.End>duration+1/fps)throw new ArgumentException("CM区間の開始・終了が録画時間の範囲外です。");
    double start=Math.Min(duration,Math.Round(r.Start*fps,MidpointRounding.AwayFromZero)/fps),end=Math.Min(duration,Math.Round(r.End*fps,MidpointRounding.AwayFromZero)/fps);
    if(end<=start)throw new ArgumentException("CM区間が1フレーム未満です。");
    if(normalized.Count>0&&start<=normalized.Last().End+0.000001)normalized.Last().End=Math.Max(normalized.Last().End,end);
    else normalized.Add(new TimeRange {Start=start,End=end,IsCommercial=true});
   }
   var plan=new CmPlan {Duration=duration,Fps=fps,Fingerprint=fingerprint,Source=source};double cursor=0;
   foreach(var r in normalized){if(r.Start>cursor)plan.Segments.Add(new TimeRange {Start=cursor,End=r.Start});plan.Segments.Add(r);cursor=r.End;}
   if(cursor<duration)plan.Segments.Add(new TimeRange {Start=cursor,End=duration});return plan;
  }
  public static CmPlan Import(string file,Metadata m,Preset preset,string fingerprint) {
   if(new FileInfo(file).Length>2*1024*1024)throw new ArgumentException("区間ファイルは2MB以下にしてください。");
   string text=File.ReadAllText(file);double fps=string.IsNullOrWhiteSpace(preset.CmTrimFps)?m.Fps:MetadataReader.Ratio(preset.CmTrimFps);
   var cuts=new List<TimeRange>();
   if(file.EndsWith(".avs",StringComparison.OrdinalIgnoreCase)) {
    text=Regex.Replace(text,@"(?m)#.*$","");var matches=Regex.Matches(text,@"Trim\s*\(\s*(\d+)\s*,\s*(\d+)\s*\)",RegexOptions.IgnoreCase);
    string remainder=Regex.Replace(text,@"Trim\s*\(\s*\d+\s*,\s*\d+\s*\)","",RegexOptions.IgnoreCase).Replace("++","").Trim();
    if(matches.Count==0||remainder.Length>0)throw new ArgumentException("Trim(start,end)++Trim(...) 形式だけを読み込めます。AVSは実行しません。");
    var kept=matches.Select(x=>new TimeRange {Start=double.Parse(x.Groups[1].Value,Inv)/fps,End=x.Groups[2].Value=="0"?m.Duration:(double.Parse(x.Groups[2].Value,Inv)+1)/fps}).OrderBy(x=>x.Start).ToList();
    double cursor=0;foreach(var r in kept){if(r.Start<cursor||r.End<=r.Start||r.End>m.Duration+1/fps)throw new ArgumentException("Trim区間が重複、逆順、または範囲外です。FPSと入力映像を確認してください。");if(r.Start>cursor+0.000001)cuts.Add(new TimeRange {Start=cursor,End=r.Start});cursor=Math.Min(r.End,m.Duration);}if(cursor<m.Duration-0.000001)cuts.Add(new TimeRange {Start=cursor,End=m.Duration});
   }else {
    foreach(string line in text.Split('\n')){string clean=line.Trim().TrimStart('\uFEFF');if(clean.Length==0||clean.StartsWith("#"))continue;var values=Regex.Split(clean,@"\s+");if(values.Length<2||values.Length>3)throw new ArgumentException("EDLは「開始秒 終了秒 0」の形式です。");string action=values.Length==3?values[2]:"0";if(action=="1"||action=="2")continue;if(action!="0"&&action!="3")throw new ArgumentException("対応していないEDL操作です。");cuts.Add(new TimeRange {Start=ParseTime(values[0]),End=ParseTime(values[1])});}
   }
   return Build(cuts,m.Duration,m.Fps,fingerprint,Path.GetFileName(file));
  }
  public static string Folder(Job j,string data)=>Path.Combine(data,"Commercials",j.Id);
  public static async Task Prepare(Job j,Settings s,string data,CancellationToken token,Action<string> log,Action changed) {
   if(j.Preset.CmMode==CmMode.Off)return;
   string fingerprint=Store.Fingerprint(j.Input),folder=Folder(j,data);Directory.CreateDirectory(folder);
   if(j.Commercials==null||j.Commercials.Fingerprint!=fingerprint) {
    string file=new[]{j.Input+".edl",Path.ChangeExtension(j.Input,".edl"),j.Input+".trim.avs",Path.ChangeExtension(j.Input,".trim.avs")}.FirstOrDefault(File.Exists);
    if(j.Preset.CmSource==CmSource.Comskip)throw new InvalidDataException("旧CM解析方式は廃止されました。プリセットでAmatsukaze方式を選択してください。");
    if(j.Preset.CmSource==CmSource.Amatsukaze)j.Commercials=await CmAnalysis.Analyze(j,s,data,token,log,changed);
    else j.Commercials=file==null?Build(Array.Empty<TimeRange>(),j.Metadata.Duration,j.Metadata.Fps,fingerprint,"手動入力待ち"):Import(file,j.Metadata,j.Preset,fingerprint);
    if(file==null&&j.Preset.CmSource==CmSource.Sidecar&&!j.Preset.CmReview)throw new InvalidDataException("CM区間ファイルがありません。確認を有効にして手動入力するか、Amatsukaze方式を選択してください。");
    j.Commercials.Approved=!j.Preset.CmReview;changed();
   }
   // Revalidate persisted intervals rather than trusting a stale or edited queue file.
   if(j.Commercials.HasVideoStart){j.Metadata.VideoStart=j.Commercials.SourceVideoStart;j.Metadata.Duration=j.Commercials.Duration;}
   var validated=Build(j.Commercials.Segments.Where(x=>x.IsCommercial),j.Metadata.Duration,j.Metadata.Fps,fingerprint,j.Commercials.Source);
   validated.HasVideoStart=j.Commercials.HasVideoStart;validated.SourceVideoStart=j.Commercials.SourceVideoStart;
   validated.Approved=j.Commercials.Approved;validated.Reviewed=j.Commercials.Reviewed;j.Commercials=validated;
   if(!validated.Approved){changed();throw new CmReviewException("CM候補を確認してください。「CM・タイムリスト」で区間を修正・確定すると続行します。");}
   if(j.Preset.CmMode==CmMode.Cut&&validated.Keep.Count==0)throw new ArgumentException("全編がCMになっています。本編を1区間以上残してください。");
   validated.ChapterFile=Path.Combine(folder,"chapters.ffmeta");File.WriteAllText(validated.ChapterFile,Chapters(validated,j.Preset.CmMode),new UTF8Encoding(false));Store.Save(Path.Combine(folder,"plan.json"),validated);log("CM区間："+validated.Segments.Count(x=>x.IsCommercial)+" / 出力方式："+j.Preset.CmMode);changed();
  }
  public static string Chapters(CmPlan plan,CmMode mode) {
   var b=new StringBuilder(";FFMETADATA1\n");double cursor=0;int main=0,cm=0;
   foreach(var r in plan.Segments){if(mode==CmMode.Cut&&r.IsCommercial)continue;double start=mode==CmMode.Cut?cursor:r.Start,end=mode==CmMode.Cut?cursor+r.End-r.Start:r.End;cursor=end;
    b.Append("[CHAPTER]\nTIMEBASE=1/1000\nSTART=").Append((long)Math.Round(start*1000)).Append("\nEND=").Append((long)Math.Round(end*1000)).Append("\ntitle=").Append(r.IsCommercial?(plan.Reviewed?"CM ":"CM候補 ")+(++cm):"本編 "+(++main)).Append('\n');
   }return b.ToString();
  }
  public static List<TimeRange> Keep(Job j)=>j.Preset.CmMode==CmMode.Cut?j.Commercials?.Keep:null;
  public static double Duration(Job j)=>j.Preset.CmMode==CmMode.Cut&&j.Commercials!=null?j.Commercials.OutputDuration:j.Metadata.Duration;
  public static string HardwareTrim(List<TimeRange> keep,double fps)=>string.Join(",",keep.Select(r=>((long)Math.Round(r.Start*fps)).ToString(Inv)+":"+Math.Max((long)Math.Round(r.Start*fps),(long)Math.Round(r.End*fps)-1).ToString(Inv)));
  public static string AudioFilter(List<TimeRange> keep,Metadata m) {
   int n=keep.Count;var b=new StringBuilder("[0:"+m.AudioIndex+"]asetpts=PTS-STARTPTS+"+N(m.AudioStart-m.VideoStart)+"/TB,aresample=async=1:first_pts=0,asplit="+n);
   for(int i=0;i<n;i++)b.Append("[ai").Append(i).Append(']');b.Append(';');
   for(int i=0;i<n;i++)b.Append("[ai").Append(i).Append("]atrim=start=").Append(N(keep[i].Start)).Append(":end=").Append(N(keep[i].End)).Append(",asetpts=PTS-STARTPTS[a").Append(i).Append("];");
   for(int i=0;i<n;i++)b.Append("[a").Append(i).Append(']');b.Append("concat=n=").Append(n).Append(":v=0:a=1,apad,atrim=duration=").Append(N(keep.Sum(x=>x.End-x.Start))).Append("[aout]");return b.ToString();
  }
  public static string Filter(List<TimeRange> keep,Metadata m,string filters) {
   var b=new StringBuilder();int n=keep.Count;b.Append("[0:").Append(m.VideoIndex).Append("]setpts=PTS-STARTPTS,split=").Append(n);for(int i=0;i<n;i++)b.Append("[vi").Append(i).Append(']');b.Append(';');
   bool audio=m.AudioIndex>=0;if(audio){b.Append("[0:").Append(m.AudioIndex).Append("]asetpts=PTS-STARTPTS+").Append(N(m.AudioStart-m.VideoStart)).Append("/TB,aresample=async=1:first_pts=0,asplit=").Append(n);for(int i=0;i<n;i++)b.Append("[ai").Append(i).Append(']');b.Append(';');}
   for(int i=0;i<n;i++){var r=keep[i];b.Append("[vi").Append(i).Append("]trim=start=").Append(N(r.Start)).Append(":end=").Append(N(r.End)).Append(",setpts=PTS-STARTPTS[v").Append(i).Append("];");if(audio)b.Append("[ai").Append(i).Append("]atrim=start=").Append(N(r.Start)).Append(":end=").Append(N(r.End)).Append(",asetpts=PTS-STARTPTS[a").Append(i).Append("];");}
   for(int i=0;i<n;i++){b.Append("[v").Append(i).Append(']');if(audio)b.Append("[a").Append(i).Append(']');}b.Append("concat=n=").Append(n).Append(":v=1:a=").Append(audio?1:0).Append("[vc]");if(audio)b.Append("[aout]");b.Append(";[vc]").Append(filters.Length==0?"null":filters).Append("[vout]");return b.ToString();
  }
  public static string RetimeSrt(string text,List<TimeRange> keep) {
   var b=new StringBuilder();int index=0;string normalized=text.Replace("\r\n","\n");
   var matches=Regex.Matches(normalized,@"(?ms)^\d+\n(\d{2,}:\d{2}:\d{2}[,.]\d{3}) --> (\d{2,}:\d{2}:\d{2}[,.]\d{3})[^\n]*\n(.*?)(?=\n\n|\z)");
   if(matches.Count==0&&text.Trim().Length>0)throw new InvalidDataException("字幕の時間情報を読み取れないためCMカットを停止しました。");
   foreach(Match match in matches){double start=ParseTime(match.Groups[1].Value.Replace(',','.')),end=ParseTime(match.Groups[2].Value.Replace(',','.')),cursor=0;foreach(var r in keep){double a=Math.Max(start,r.Start),z=Math.Min(end,r.End);if(z>a)b.Append(++index).Append('\n').Append(Time(cursor+a-r.Start).Replace('.',',')).Append(" --> ").Append(Time(cursor+z-r.Start).Replace('.',',')).Append('\n').Append(match.Groups[3].Value.Trim()).Append("\n\n");cursor+=r.End-r.Start;}}
   return b.ToString();
  }
  public static string CutSubtitles(Job j,string subtitle,string folder,Action<string> log) {
   if(subtitle==null)return null;
   try {string text=RetimeSrt(File.ReadAllText(subtitle),j.Commercials.Keep);if(text.Length==0)return null;string output=Path.Combine(folder,"cut-subtitles.srt");File.WriteAllText(output,text,new UTF8Encoding(false));return output;}
   catch(InvalidDataException)when(j.Preset.IgnoreSubtitleErrors){Subtitles.Warning(j,"字幕の時間変換に失敗したため、字幕なしでCMカットを続行します。",log);j.Metadata.SubtitleStatus="字幕の時間変換を省略";return null;}
  }
  public static void Export(Job j) {
   if(j.Commercials==null||j.Preset.CmMode==CmMode.Off)return;var b=new StringBuilder("入力開始,入力終了,区分,出力開始,出力終了\r\n");double cursor=0;
   foreach(var r in j.Commercials.Segments){bool cut=j.Preset.CmMode==CmMode.Cut&&r.IsCommercial;double start=j.Preset.CmMode==CmMode.Cut?cursor:r.Start,end=start+r.End-r.Start;b.Append(Time(r.Start)).Append(',').Append(Time(r.End)).Append(',').Append(r.IsCommercial?(j.Commercials.Reviewed?"CM":"CM候補"):"本編").Append(',').Append(cut?"":Time(start)).Append(',').Append(cut?"":Time(end)).Append("\r\n");if(!cut)cursor=end;}
   File.WriteAllText(j.Output+".cm.csv",b.ToString(),new UTF8Encoding(true));Store.Save(j.Output+".cm.json",j.Commercials);
  }
 }
}
