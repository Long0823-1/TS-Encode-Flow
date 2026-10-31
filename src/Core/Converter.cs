using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public static class Converter {
  public static double? EncoderProgress(string line){var m=Regex.Match(line,@"^\s*\[?(\d+(?:\.\d+)?)%");return m.Success?MetadataReader.Number(m.Groups[1].Value):(double?)null;}
  public static bool WritableRecording(string file) {
   try{using(var f=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.Read)){return f.Length>0;}}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
  }
  public static async Task Convert(Job j,Settings settings,CancellationToken token,Action<string> log,Action changed) {
   Commands.Validate(j.Preset);if(!WritableRecording(j.Input))throw new IOException("入力が録画中または読み取りできません。");
   string fingerprint=Store.Fingerprint(j.Input);
   j.Metadata=await MetadataReader.Read(j.Input,settings,j.Preset.ServiceId,token,log);changed();
   j.Message="字幕・DRCSを確認中";changed();
   string dataFolder=Path.GetDirectoryName(Path.GetDirectoryName(j.LogFile));
   string subtitle=await Subtitles.Prepare(j,settings,dataFolder,token,log,changed);
   string root=Path.GetFullPath(j.OutputFolder),relative=Names.Render(j.FileName,j.Metadata,j.Preset);
   string desired=Path.GetFullPath(Path.Combine(root,relative));if(!desired.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("出力先が保存フォルダーの範囲外です。");
   if(desired.Length>200)throw new ArgumentException("出力パスが長すぎます。マクロや保存先を短くしてください。");
   Directory.CreateDirectory(Path.GetDirectoryName(desired));
   string final=null,reserve=null,temp=null,selected=null,muxed=null;FileStream reservation=null;
   try {
    for(int n=0;n<10000;n++) {
     string name=n==0?desired:Path.Combine(Path.GetDirectoryName(desired),Path.GetFileNameWithoutExtension(desired)+" ("+n+")"+Path.GetExtension(desired));
     if(File.Exists(name))continue;
     try{reservation=File.Open(name+".reserve",FileMode.CreateNew,FileAccess.Write,FileShare.None);reserve=name+".reserve";final=name;break;}catch(IOException){}
    }
    if(final==null)throw new IOException("出力名を確保できません。");
    temp=Path.Combine(Path.GetDirectoryName(final),"."+Path.GetFileNameWithoutExtension(final)+".work-"+j.Id+Path.GetExtension(final));
    var s=Store.Clone(settings);var p=j.Preset;
    Action<string> progress=line=> {
     log(line);var percent=EncoderProgress(line);
     if(percent.HasValue)j.Progress=Math.Min(99,percent.Value);
     if(line.StartsWith("out_time_us=",StringComparison.Ordinal)&&j.Metadata.Duration>0)j.Progress=Math.Min(99,MetadataReader.Number(line.Substring(12))/1000000/j.Metadata.Duration*100);
    };
    Command command;
    if(p.Engine==Engine.CPU)command=Commands.Ffmpeg(s,p,j.Metadata,j.Input,temp);
    else {
     string encoder=p.Engine==Engine.NVEncC?s.Nvenc:p.Engine==Engine.VCEEncC?s.Vceenc:s.Qsvenc;if(!File.Exists(encoder))throw new FileNotFoundException("選択したGPUエンコーダーがありません。設定タブでパスを指定してください。",encoder);
     bool prefiltered=!string.IsNullOrWhiteSpace(p.Fps);
     selected=Path.Combine(Path.GetDirectoryName(final),".selected-"+j.Id+(prefiltered?".mkv":".ts"));
     j.Message=prefiltered?"FPS指定のためFFmpegで可逆前処理中":"対象サービスの映像・音声を分離中";changed();
     var demux=Commands.Ffmpeg(s,p,j.Metadata,j.Input,selected,prefiltered,!prefiltered);
     log(demux.ToString());var result=await Processes.Run(demux.Exe,demux.Args,token,log);
     if(result.ExitCode!=0)throw new InvalidDataException("前処理失敗：終了コード"+result.ExitCode+"\n"+Tail(result.Error));
     command=Commands.Hardware(s,p,j.Metadata,selected,temp,prefiltered);
    }
    j.Command=command.ToString();j.Message="変換中："+p.Engine;log(j.Command);changed();
    var encoded=await Processes.Run(command.Exe,command.Args,token,progress);
    if(encoded.ExitCode!=0)throw new InvalidDataException("変換失敗：終了コード"+encoded.ExitCode+"\n"+Tail(encoded.Error));
    if(p.EmbedMetadata||subtitle!=null) {
     j.Message="字幕・番組情報を動画に格納中";changed();muxed=Path.Combine(Path.GetDirectoryName(final),".mux-"+j.Id+Path.GetExtension(final));
     subtitle=await Subtitles.Pack(j,s,temp,subtitle,muxed,token,log);File.Delete(temp);temp=muxed;muxed=null;
    }
    token.ThrowIfCancellationRequested();if(!File.Exists(temp)||new FileInfo(temp).Length<1024)throw new InvalidDataException("出力が空です。");
    j.Message="出力ファイルを検証中";changed();
    var validate=await Processes.Run(s.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",temp},token,null,60);
    if(validate.ExitCode!=0)throw new InvalidDataException("出力を読み取れません。");
    var data=Store.Json.Deserialize<Dictionary<string,object>>(validate.Output);
    var video=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).FirstOrDefault(x=>MetadataReader.Text(x,"codec_type")=="video");
    if(video==null||MetadataReader.Int(video,"width")==0)throw new InvalidDataException("出力に有効な映像がありません。");
    if(j.Metadata.AudioIndex>=0&&!MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).Any(x=>MetadataReader.Text(x,"codec_type")=="audio"))throw new InvalidDataException("出力から音声が失われています。");
    if(subtitle!=null&&!MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).Any(x=>MetadataReader.Text(x,"codec_type")=="subtitle")) {
     if(!p.IgnoreSubtitleErrors)throw new InvalidDataException("出力から字幕が失われています。");
     Subtitles.Warning(j,"出力に字幕がないため、字幕なしとして保存します。",log);j.Metadata.SubtitleStatus="字幕なしで保存";subtitle=null;
    }
    var expected=Commands.Resolution(p,j.Metadata);if(expected!=null&&(MetadataReader.Int(video,"width")!=expected[0]||MetadataReader.Int(video,"height")!=expected[1]))throw new InvalidDataException("出力の解像度が指定と一致しません。");
    double duration=MetadataReader.Number(MetadataReader.Text(MetadataReader.Obj(data.ContainsKey("format")?data["format"]:null),"duration"));
    if(j.Metadata.Duration>5&&duration<j.Metadata.Duration*0.9-2)throw new InvalidDataException("出力時間が入力より短すぎます。");
    if(Store.Fingerprint(j.Input)!=fingerprint)throw new IOException("処理中に入力ファイルが更新されました。出力を確定しません。");
    File.Move(temp,final);temp=null;j.Output=final;j.Progress=100;
    Store.Save(final+".encode.json",new {input=j.Input,output=final,preset=p,metadata=j.Metadata,command=j.Command,finished=DateTime.Now,validation=MetadataReader.Obj(data.ContainsKey("format")?data["format"]:null)});
    j.Message=!string.IsNullOrEmpty(j.Metadata.SubtitleWarning)?"完了（字幕の警告あり・元TSを保持）":j.Preset.KeepSubtitles&&subtitle==null?"完了（"+j.Metadata.SubtitleStatus+"・元TSを保持）":"完了（元TSを保持）";log(j.Message+"："+final);
   }finally {
    reservation?.Dispose();foreach(string ownFile in new[]{reserve,temp,selected,muxed})if(ownFile!=null&&File.Exists(ownFile))try{File.Delete(ownFile);}catch(IOException){}
   }
  }
  public static string Tail(string text)=>text.Length>1800?text.Substring(text.Length-1800):text;
 }
}
