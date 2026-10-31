using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class DrcsGlyph {public string Hash,Image,Alternative="",Context="";public double TimeSeconds;}
 public sealed class DrcsReplacement {public string Text="",ReferenceImage="";public DateTime Updated;}
 public sealed class CaptionCue {public long Pts,Duration;public string Text="";}
 public sealed class CaptionScan {public int SchemaVersion,DecodeErrors;public long TransportPackets,SubtitlePackets;public List<CaptionCue> Cues=new();public List<DrcsGlyph> Glyphs=new();}
 public sealed class DrcsPendingException:Exception {public DrcsPendingException(string text):base(text){}}
 public static class Drcs {
  public static string MapFile(string data)=>Path.Combine(data,"Drcs","replacements.json");
  public static Dictionary<string,DrcsReplacement> Load(string data)=>Store.Load(MapFile(data),()=>new Dictionary<string,DrcsReplacement>());
  public static bool Resolved(DrcsGlyph g,Dictionary<string,DrcsReplacement> map)=>map.TryGetValue(g.Hash,out var value)&&!string.IsNullOrWhiteSpace(value.Text)||!string.IsNullOrWhiteSpace(g.Alternative);
  public static void Save(string data,string hash,string text,string reference="") {
   if(string.IsNullOrWhiteSpace(text)||text.Length>32||text.Any(char.IsControl))throw new ArgumentException("代替文字を1～32文字で入力してください（改行不可）。");
   if(!Regex.IsMatch(hash,@"^[a-fA-F0-9]{32}_\d+_\d+_\d+$"))throw new ArgumentException("DRCSの識別情報が不正です。");
   var map=Load(data);map[hash]=new DrcsReplacement {Text=text,ReferenceImage=reference??"",Updated=DateTime.Now};Store.Save(MapFile(data),map);
  }
  public static string Resolve(string text,IEnumerable<DrcsGlyph> glyphs,Dictionary<string,DrcsReplacement> map) {
   var byHash=glyphs.ToDictionary(g=>g.Hash);return Regex.Replace(text,@"\{\{DRCS:([a-fA-F0-9_]+)\}\}",m=>map.TryGetValue(m.Groups[1].Value,out var replacement)&&!string.IsNullOrWhiteSpace(replacement.Text)?replacement.Text:byHash.TryGetValue(m.Groups[1].Value,out var glyph)&&!string.IsNullOrWhiteSpace(glyph.Alternative)?glyph.Alternative:"�");
  }
 }
 public static class Subtitles {
  public static string Srt(CaptionScan scan,Metadata metadata,Dictionary<string,DrcsReplacement> map) {
   var output=new StringBuilder();int count=0;var cues=scan.Cues.OrderBy(c=>c.Pts).ToList();
   for(int i=0;i<cues.Count;i++) {
    var cue=cues[i];string text=Drcs.Resolve(cue.Text,scan.Glyphs,map).Trim();if(text.Length==0)continue;
    double start=cue.Pts/1000.0-metadata.VideoStart;
    double end=cue.Duration>=0?start+cue.Duration/1000.0:i+1<cues.Count?cues[i+1].Pts/1000.0-metadata.VideoStart:metadata.Duration;
    if(metadata.Duration>0)end=Math.Min(end,metadata.Duration);start=Math.Max(0,start);if(end<=start)continue;
    output.AppendLine((++count).ToString());output.AppendLine(Time(start)+" --> "+Time(end));output.AppendLine(text);output.AppendLine();
   }return output.ToString();
  }
  static string Time(double seconds){var t=TimeSpan.FromMilliseconds(Math.Max(0,seconds*1000));return ((int)t.TotalHours).ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00")+","+t.Milliseconds.ToString("000");}
  public static async Task<string> Prepare(Job j,Settings s,string data,CancellationToken token,Action<string> log,Action changed) {
   j.Metadata.SubtitleWarning="";
   try{return await PrepareCore(j,s,data,token,log,changed);}
   catch(Exception e)when(j.Preset.IgnoreSubtitleErrors&&(e is IOException||e is InvalidDataException||e is UnauthorizedAccessException||e is TimeoutException||e is System.ComponentModel.Win32Exception)) {
    token.ThrowIfCancellationRequested();Warning(j,"字幕を省略："+e.Message,log);j.Metadata.SubtitleStatus="字幕エラーを無視して字幕なしで続行";changed();return null;
   }
  }
  public static void Warning(Job j,string message,Action<string> log){j.Metadata.SubtitleWarning=message;log("字幕の警告："+message);}
  static async Task<string> PrepareCore(Job j,Settings s,string data,CancellationToken token,Action<string> log,Action changed) {
   if(!j.Preset.KeepSubtitles){j.Metadata.SubtitleStatus="字幕保存OFF";return null;}if(j.Metadata.SubtitleIndex<0){j.Metadata.SubtitleStatus="字幕トラックなし";return null;}
   string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Store.Fingerprint(j.Input)+"|"+j.Metadata.SubtitleIndex)));
   string folder=Path.Combine(data,"Drcs","Scans",hash);Directory.CreateDirectory(folder);string subtitle=Path.Combine(folder,"subtitles.srt");
   if(j.Metadata.SubtitleCodec=="arib_caption") {
    if(!File.Exists(s.CaptionTool))throw new FileNotFoundException("字幕解析ツールがありません。配布ZIP全体を展開してください。",s.CaptionTool);
    string file=Path.Combine(folder,"captions.json");CaptionScan scan=null;
    if(File.Exists(file))try{scan=Store.Load<CaptionScan>(file,()=>new());}catch(InvalidDataException){}
    if(scan!=null&&scan.SchemaVersion<2)scan=null;
    if(scan==null) {
     var result=await Processes.Run(s.CaptionTool,new[]{j.Input,j.Metadata.SubtitlePid.ToString(),folder},token,log);
     if(result.ExitCode!=0)throw new InvalidDataException("ARIB字幕の解析に失敗しました。終了コード："+result.ExitCode);
     scan=Store.Load<CaptionScan>(file,()=>throw new InvalidDataException("字幕解析結果がありません。"));
    }
    foreach(var glyph in scan.Glyphs){if(!Path.IsPathRooted(glyph.Image))glyph.Image=Path.Combine(folder,glyph.Image);var first=scan.Cues.FirstOrDefault(c=>c.Text.Contains("{{DRCS:"+glyph.Hash+"}}"));glyph.TimeSeconds=first==null?0:Math.Max(0,first.Pts/1000.0-j.Metadata.VideoStart);}
    j.Glyphs=scan.Glyphs;changed();var map=Drcs.Load(data);int unknown=scan.Glyphs.Count(g=>!Drcs.Resolved(g,map));
    if(unknown>0) {
     if(!j.Preset.IgnoreSubtitleErrors)throw new DrcsPendingException("未対応のDRCSが"+unknown+"字あります。「字幕・DRCS」で画像と代替文字を確認してください。");
     Warning(j,"未対応のDRCS "+unknown+"字を � に置き換えて続行します。",log);
    }
    j.Metadata.SubtitlePackets=scan.SubtitlePackets;
    string text=Srt(scan,j.Metadata,map);if(text.Length==0){if(scan.SubtitlePackets>0&&scan.DecodeErrors>0)throw new InvalidDataException("字幕データを正常にデコードできませんでした。字幕を黙って破棄せず停止します。");j.Metadata.SubtitleStatus=scan.SubtitlePackets==0?"字幕の実データなし":"表示する字幕文なし";log(j.Metadata.SubtitleStatus+"（字幕PIDのTSパケット数："+scan.SubtitlePackets+"）");return null;}
    j.Metadata.SubtitleStatus="日本語字幕を保存";File.WriteAllText(subtitle,text,new UTF8Encoding(false));log("ARIB字幕を抽出し、保存済みDRCS対応表を適用しました。");return subtitle;
   }
   var args=new[]{"-hide_banner","-nostdin","-y","-i",j.Input,"-map","0:"+j.Metadata.SubtitleIndex,"-c:s","srt",subtitle};
   var extraction=await Processes.Run(s.Ffmpeg,args,token,log);if(extraction.ExitCode!=0)throw new InvalidDataException("字幕をテキスト化できません。この字幕形式に対応したFFmpegが必要です。");return new FileInfo(subtitle).Length>0?subtitle:null;
  }
  public static Command Mux(Settings s,Preset p,Metadata m,string video,string subtitle,string output,string chapterFile=null) {
   var c=new Command {Exe=s.Ffmpeg};var a=c.Args;a.AddRange(new[]{"-hide_banner","-nostdin","-y","-i",video});
   if(subtitle!=null)a.AddRange(new[]{"-i",subtitle});if(chapterFile!=null)a.AddRange(new[]{"-f","ffmetadata","-i",chapterFile});a.AddRange(new[]{"-map","0:v:0","-map","0:a?","-c:v","copy","-c:a","copy"});
   if(subtitle!=null)a.AddRange(new[]{"-map","1:0","-c:s",p.Container==Container.mp4?"mov_text":"ass","-metadata:s:s:0","language=jpn","-metadata:s:s:0","title=日本語","-disposition:s:0","0"});
   if(chapterFile!=null)a.AddRange(new[]{"-map_chapters",subtitle==null?"1":"2"});
   else a.AddRange(new[]{"-map_metadata","-1"});
   if(p.EmbedMetadata) {
    var date=new DateTimeOffset(DateTime.SpecifyKind(m.Start,DateTimeKind.Unspecified),TimeSpan.FromHours(9));
    foreach(var item in new Dictionary<string,string>{{"title",m.Title},{"show",m.Title},{"network",m.Channel},{"description",m.Description},{"comment",m.Channel+" / "+m.Start.ToString("yyyy/MM/dd HH:mm")+"\n"+m.Description+"\nServiceID: "+m.ServiceId+" / EventID: "+m.EventId},{"date",m.Start.ToString("yyyy-MM-dd")},{"creation_time",date.ToUniversalTime().ToString("O")},{"episode_id",m.Episode},{"service_id",m.ServiceId.ToString()},{"event_id",m.EventId.ToString()}})if(!string.IsNullOrEmpty(item.Value))a.AddRange(new[]{"-metadata",item.Key+"="+item.Value});
   }
   if(p.Container==Container.mp4){a.AddRange(new[]{"-movflags","+faststart+use_metadata_tags"});if(p.Codec==Codec.HEVC)a.AddRange(new[]{"-tag:v","hvc1"});}
   a.Add(output);return c;
  }
  public static async Task<string> Pack(Job j,Settings s,string video,string subtitle,string output,CancellationToken token,Action<string> log,Func<Command,Task<ProcessResult>> run=null) {
   run??=c=>Processes.Run(c.Exe,c.Args,token,log);
   var command=Mux(s,j.Preset,j.Metadata,video,subtitle,output,j.Preset.CmMode==CmMode.Off?null:j.Commercials?.ChapterFile);log(command.ToString());var packed=await run(command);
   if(packed.ExitCode!=0&&subtitle!=null&&j.Preset.IgnoreSubtitleErrors) {
    token.ThrowIfCancellationRequested();Warning(j,"字幕の格納に失敗したため、字幕なしで格納し直します。",log);j.Metadata.SubtitleStatus="字幕格納エラーを無視して字幕なしで続行";
    subtitle=null;command=Mux(s,j.Preset,j.Metadata,video,null,output,j.Preset.CmMode==CmMode.Off?null:j.Commercials?.ChapterFile);log(command.ToString());packed=await run(command);
   }
   if(packed.ExitCode!=0)throw new InvalidDataException("字幕・メタデータの格納失敗："+Converter.Tail(packed.Error));
   return subtitle;
  }
 }
}
