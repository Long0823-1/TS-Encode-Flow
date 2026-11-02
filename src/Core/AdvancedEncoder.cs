using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class AdvancedEncoder {
  public static double SourceStart(Metadata m,double videoSeconds)=>Math.Max(0,videoSeconds+m.VideoStart-m.FormatStart);
  public static string Hash(string file){using var f=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(f));}
  public static string Key(Job j,Settings s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("advanced-1|"+Store.Fingerprint(j.Input)+"|"+Store.Json.Serialize(j.Preset)+"|"+Store.Json.Serialize(Commercials.Keep(j))+"|"+Tool(s.Ffmpeg)+"|"+Tool(s.Ffprobe)+"|"+Tool(j.Preset.Engine==Engine.CPU?s.Ffmpeg:j.Preset.Engine==Engine.NVEncC?s.Nvenc:j.Preset.Engine==Engine.VCEEncC?s.Vceenc:s.Qsvenc)+"|"+Hash(string.IsNullOrEmpty(typeof(AdvancedEncoder).Assembly.Location)?Environment.ProcessPath:typeof(AdvancedEncoder).Assembly.Location))));
  static string Tool(string path)=>File.Exists(path)?Store.Fingerprint(path)+"|"+Hash(path):path;
  public static List<EncodePart> Parts(Job j) {
   var result=new List<EncodePart>();double cursor=0;var ranges=Commercials.Keep(j)??new(){new(){Start=0,End=j.Metadata.Duration}};
   double span=j.Preset.Cadence==CadenceMode.MixedVfr?12:j.Preset.ResumeChunks?j.Preset.ChunkSeconds:double.MaxValue;
   foreach(var r in ranges)for(double start=r.Start;start<r.End-0.00001;){double end=Math.Min(r.End,start+span);if(r.End-end<0.5)end=r.End;result.Add(new(){Index=result.Count,Start=start,End=end,OutputStart=cursor});cursor+=end-start;start=end;}
   return result;
  }
  public static async Task<string> Cadence(Job j,Settings s,EncodePart part,CancellationToken token,Action<string> log) {
   if(j.Preset.Cadence!=CadenceMode.MixedVfr)return "preset";
   if(Math.Abs(j.Metadata.Fps-30000.0/1001)>0.015)return new[]{"tt","bb","tb","bt"}.Contains(j.Metadata.FieldOrder)?"video":"progressive";
   var evidence=await Telecine.Probe(j,s,part.Start,Math.Min(12,part.End-part.Start),token,log,false);
   int frames=evidence.DetectedTop+evidence.DetectedBottom+evidence.DetectedProgressive;bool interlaced=frames>0&&(double)Math.Max(evidence.DetectedTop,evidence.DetectedBottom)/frames>0.90;
   string mode=evidence.Film?"film":!interlaced&&(j.Metadata.FieldOrder=="progressive"||frames>0&&(double)evidence.DetectedProgressive/frames>0.85)?"progressive":"video";
   log("混在FPS："+Commercials.N(part.Start)+"–"+Commercials.N(part.End)+"秒 / "+mode+" / 3:2="+evidence.Cadence.ToString("P1",CultureInfo.InvariantCulture));return mode;
  }
  static List<string> Input(Job j,EncodePart part)=>new(){"-hide_banner","-nostdin","-y","-ss",Commercials.N(SourceStart(j.Metadata,part.Start)),"-i",j.Input,"-t",Commercials.N(part.End-part.Start),"-map","0:"+j.Metadata.VideoIndex,"-an","-sn","-dn"};
  public static async Task EncodeVideo(Job j,Settings s,EncodePart part,string output,CancellationToken token,Action<string> log) {
   string filters=AdvancedFilters.Timeline(j.Preset,j.Metadata,part.Mode);var p=Store.Clone(j.Preset);p.Container=Container.mkv;p.ExtraArgs="";
   if(p.Engine==Engine.CPU) {
    var c=Commands.Ffmpeg(s,p,j.Metadata,j.Input,output);var a=Input(j,part);a.AddRange(new[]{"-vf",filters,"-c:v",p.Codec==Codec.H264?"libx264":p.Codec==Codec.HEVC?"libx265":"libsvtav1"});
    if(p.Speed.Length>0)a.AddRange(new[]{"-preset",p.Speed});a.AddRange(p.RateMode==RateMode.Quality?new[]{"-crf",p.Quality.ToString(CultureInfo.InvariantCulture)}:new[]{"-b:v",p.Bitrate+"k"});
    if(p.RateMode==RateMode.CBR)a.AddRange(new[]{"-minrate",p.Bitrate+"k","-maxrate",p.Bitrate+"k","-bufsize",(p.Bitrate*2)+"k"});
    if(p.Threads>0)a.AddRange(new[]{"-threads",p.Threads.ToString(CultureInfo.InvariantCulture)});
    a.AddRange(new[]{"-pix_fmt",p.BitDepth==10?"yuv420p10le":"yuv420p","-fps_mode","passthrough",output});c.Args=a;log(c.ToString());var r=await Processes.Run(c.Exe,c.Args,token,log);if(r.ExitCode!=0)throw new InvalidDataException("区間エンコード失敗："+Converter.Tail(r.Error));
   }else {
    bool pipe=p.PipeGpu||p.Cadence==CadenceMode.MixedVfr;string intermediate=output+".ffv1.mkv";
    var source=new Command {Exe=s.Ffmpeg,Args=Input(j,part)};source.Args.AddRange(new[]{"-vf",filters,"-pix_fmt",p.BitDepth==10?"yuv420p10le":"yuv420p","-fps_mode","passthrough"});
    source.Args.AddRange(pipe?new[]{"-strict","-1","-f","yuv4mpegpipe","pipe:1"}:new[]{"-c:v","ffv1","-level","3",intermediate});
    var m=Store.Clone(j.Metadata);m.AudioIndex=-1;var encoder=Commands.Hardware(s,p,m,pipe?"-":intermediate,output,true);
    if(pipe){encoder.Args.Remove("--avsw");encoder.Args.Insert(0,"--y4m");}
    if(p.BitDepth==10)encoder.Args.InsertRange(0,new[]{"--output-depth","10"});
    encoder.Args.InsertRange(0,ColorArgs(p,m));
    try {
     if(pipe){log(source+" | "+encoder);await PipeEncoding.Run(source,encoder,token,log);}
     else {log(source.ToString());var a=await Processes.Run(source.Exe,source.Args,token,log);if(a.ExitCode!=0)throw new InvalidDataException("可逆前処理に失敗："+Converter.Tail(a.Error));log(encoder.ToString());var b=await Processes.Run(encoder.Exe,encoder.Args,token,log);if(b.ExitCode!=0)throw new InvalidDataException("GPU変換に失敗："+Converter.Tail(b.Error));}
    }finally{if(File.Exists(intermediate))File.Delete(intermediate);}
   }
  }
  public static List<string> ColorArgs(Preset p,Metadata m) {
   var args=new List<string>();bool converted=p.ColorMatrix!=VideoMatrix.Preserve;
   string matrix=converted?(p.ColorMatrix==VideoMatrix.Bt2020nc?"bt2020nc":AdvancedFilters.Matrix(p.ColorMatrix)):m.ColorSpace;
   string primaries=converted?(p.ColorMatrix==VideoMatrix.Bt2020nc?"bt2020":AdvancedFilters.Matrix(p.ColorMatrix)):m.ColorPrimaries;
   string transfer=converted?(p.ColorMatrix==VideoMatrix.Bt2020nc?"bt2020_10":p.ColorMatrix==VideoMatrix.Smpte170m?"smpte170m":"bt709"):m.ColorTransfer.Replace('-','_');
   foreach(var pair in new[]{("--colormatrix",matrix),("--colorprim",primaries),("--transfer",transfer)})if(!string.IsNullOrWhiteSpace(pair.Item2)&&pair.Item2!="unknown")args.AddRange(new[]{pair.Item1,pair.Item2});
   string range=p.ColorRange==VideoRange.Full?"full":p.ColorRange==VideoRange.Limited?"limited":m.ColorRange=="pc"?"full":m.ColorRange=="tv"?"limited":"";if(range.Length>0)args.AddRange(new[]{"--colorrange",range});
   double sar=Commands.Resolution(p,m)==null?m.Sar:1;int numerator=(int)Math.Round(sar*10000),denominator=10000;int a=numerator,b=denominator;while(b!=0){int t=a%b;a=b;b=t;}args.AddRange(new[]{"--sar",(numerator/Math.Max(1,a))+":"+(denominator/Math.Max(1,a))});return args;
  }
  public static async Task ValidateVideo(Settings s,string file,double duration,CancellationToken token,bool decode=false) {
   var r=await Processes.Run(s.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",file},token,null,60);if(r.ExitCode!=0)throw new InvalidDataException("区間を読み取れません。");
   var d=Store.Json.Deserialize<Dictionary<string,object>>(r.Output);var video=MetadataReader.Items(d,"streams").Select(MetadataReader.Obj).FirstOrDefault(x=>MetadataReader.Text(x,"codec_type")=="video");
   double seconds=MetadataReader.Number(MetadataReader.Text(MetadataReader.Obj(d.ContainsKey("format")?d["format"]:null),"duration"));
   if(video==null||MetadataReader.Int(video,"width")<2)throw new InvalidDataException("区間に有効な映像がありません："+Path.GetFileName(file));
   if(Math.Abs(seconds-duration)>Math.Max(0.3,2.0/24))throw new InvalidDataException("映像区間の長さが一致しません：予定 "+Commercials.N(duration)+"秒 / 出力 "+Commercials.N(seconds)+"秒 / "+Path.GetFileName(file)+"。入力の時刻飛び・デコード警告とログを確認してください。");
   if(decode){var check=await Processes.Run(s.Ffmpeg,new[]{"-v","error","-xerror","-i",file,"-map","0:v:0","-f","null","-"},token);if(check.ExitCode!=0)throw new InvalidDataException("保存済み区間を最後までデコードできません。");}
  }
  public static async Task Run(Job j,Settings s,string data,string output,CancellationToken token,Action<string> log,Action changed) {
   AdvancedFilters.InputColor(j.Preset,j.Metadata);AdvancedFilters.Video(j.Preset,j.Metadata);
   string key=Key(j,s),folder=Path.Combine(EncodeCache.Register(data,s,j),j.Id,key);Directory.CreateDirectory(folder);
   using var lease=File.Open(Path.Combine(folder,".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
   string manifest=Path.Combine(folder,"manifest.json");var old=Store.Load(manifest,()=>new AdvancedRun());var run=new AdvancedRun {Key=key,CacheFolder=folder,Parts=Parts(j)};j.Advanced=run;
   foreach(var part in run.Parts) {
    token.ThrowIfCancellationRequested();part.File=Path.Combine(folder,"part-"+part.Index.ToString("D5")+".mkv");var saved=old.Key==key?old.Parts.FirstOrDefault(x=>x.Index==part.Index&&x.Start==part.Start&&x.End==part.End&&x.Complete):null;
    if(j.Preset.ResumeChunks&&saved!=null&&File.Exists(part.File)&&saved.Hash==Hash(part.File)) {
     try{await ValidateVideo(s,part.File,part.End-part.Start,token,true);part.Mode=saved.Mode;part.Hash=saved.Hash;part.Complete=part.Reused=true;run.ReusedParts++;log("保存済み区間を再利用："+part.Index);}catch(InvalidDataException){part.Complete=false;}
    }
    if(!part.Complete) {
     part.Mode=await Cadence(j,s,part,token,log);j.Message="区間 "+(part.Index+1)+" / "+run.Parts.Count+"："+part.Mode;changed();string temp=part.File+".partial.mkv";
     try{await EncodeVideo(j,s,part,temp,token,log);await ValidateVideo(s,temp,part.End-part.Start,token);token.ThrowIfCancellationRequested();File.Move(temp,part.File,true);part.Hash=Hash(part.File);part.Complete=true;}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    Store.Save(manifest,run);j.Progress=Math.Min(90,90.0*(part.Index+1)/run.Parts.Count);changed();
   }
   token.ThrowIfCancellationRequested();j.Message="映像区間と音声トラックを結合中";changed();
   await Mux(j,s,run,folder,output,token,log);j.Command="区間パイプライン："+manifest;
   if(!j.Preset.ResumeChunks)foreach(var part in run.Parts)if(File.Exists(part.File))File.Delete(part.File);
  }
  public static async Task Mux(Job j,Settings s,AdvancedRun run,string folder,string output,CancellationToken token,Action<string> log) {
   string list=Path.Combine(folder,"video.ffconcat");var text=new StringBuilder("ffconcat version 1.0\n");foreach(var part in run.Parts)text.Append("file '").Append(Path.GetFileName(part.File)).Append("'\nduration ").Append(Commercials.N(part.End-part.Start)).Append('\n');File.WriteAllText(list,text.ToString(),new UTF8Encoding(false));
   var args=new List<string>{"-hide_banner","-nostdin","-y","-f","concat","-safe","1","-i",list};var tracks=AdvancedOptions.Audio(j.Preset,j.Metadata);var keep=Commercials.Keep(j);var af=new StringBuilder();
   if(keep==null&&j.Preset.CopyAudio)args.AddRange(new[]{"-ss",Commercials.N(SourceStart(j.Metadata,0))});args.AddRange(MetadataReader.ProbeInput());args.AddRange(new[]{"-i",j.Input,"-map","0:v:0","-c:v","copy"});
   for(int i=0;i<tracks.Count;i++) {
    var track=tracks[i];string spec=track.Pid>0?"1:i:0x"+track.Pid.ToString("x",CultureInfo.InvariantCulture):"1:"+track.Index;if(keep==null&&j.Preset.CopyAudio)args.AddRange(new[]{"-map",spec});else {var ranges=keep??new List<TimeRange>{new(){Start=0,End=j.Metadata.Duration}};var m=Store.Clone(j.Metadata);m.AudioIndex=track.Index;m.AudioStart=track.Start;string f=Commercials.AudioFilter(ranges,m).Replace("[0:"+track.Index+"]","["+spec+"]");foreach(string label in new[]{"ai","a"})for(int k=ranges.Count-1;k>=0;k--)f=f.Replace("["+label+k+"]","[t"+i+label+k+"]");f=f.Replace("[aout]","[ao"+i+"]");if(af.Length>0)af.Append(';');af.Append(f);args.AddRange(new[]{"-map","[ao"+i+"]"});}
    args.AddRange(new[]{"-metadata:s:a:"+i,"language="+track.Language,"-metadata:s:a:"+i,"title="+track.Title,"-disposition:a:"+i,(track.Default?"default":"0")+(track.Commentary?"+comment":"")});
   }
   if(af.Length>0){string filter=Path.Combine(folder,"audio-filter.txt");File.WriteAllText(filter,af.ToString());args.AddRange(new[]{"-/filter_complex",filter});}
   args.AddRange(new[]{"-c:a",j.Preset.CopyAudio&&keep==null?"copy":"aac"});if(!j.Preset.CopyAudio||keep!=null)args.AddRange(new[]{"-b:a",j.Preset.AudioBitrate+"k"});
   args.AddRange(new[]{"-map_metadata","-1","-map_chapters","-1","-t",Commercials.N(Commercials.Duration(j))});if(j.Preset.Container==Container.mp4&&j.Preset.Codec==Codec.HEVC)args.AddRange(new[]{"-tag:v","hvc1"});args.Add(output);
   var mux=await Processes.Run(s.Ffmpeg,args,token,log);if(mux.ExitCode!=0)throw new InvalidDataException("区間・複数音声の結合失敗："+Converter.Tail(mux.Error));
  }
 }
}
