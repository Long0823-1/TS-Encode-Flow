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
   j.WorkingInput="";j.TimelineRepair=null;Commands.Validate(j.Preset);if(!WritableRecording(j.Input))throw new IOException("入力が録画中または読み取りできません。");
   string fingerprint=Store.Fingerprint(j.Input);
   j.Metadata=await MetadataReader.Read(j.Input,settings,j.Preset.ServiceId,token,log);if(Store.Fingerprint(j.Input)!=fingerprint)throw new IOException("入力解析中にTSが変更されました。再試行してください。");j.Fingerprint=fingerprint;changed();
   j.Message="字幕・DRCSを確認中";changed();
   string dataFolder=Path.GetDirectoryName(Path.GetDirectoryName(j.LogFile));
   if(j.Preset.DiagnoseSignal) {
    j.Message="TSのパケット・時刻を診断中";changed();string report=Path.Combine(dataFolder,"Diagnostics",j.Id+".json");
    j.Signal=Store.Load(report,()=>new SignalReport());if(j.Signal.Fingerprint!=fingerprint||j.Signal.VideoIndex!=j.Metadata.VideoIndex||j.Signal.AudioIndices!=string.Join(",",j.Metadata.AudioTracks.Select(x=>x.Index))){j.Signal=await SignalDiagnostics.Analyze(j.Input,j.Metadata,settings,token,log);Store.Save(report,j.Signal);}changed();
   }
   await TimelineRepair.Prepare(j,settings,dataFolder,token,log,changed);
   j.Message="CM区間を確認中";changed();await Commercials.Prepare(j,settings,dataFolder,token,log,changed);
   var keep=Commercials.Keep(j);double expectedDuration=Commercials.Duration(j);
   j.Message="逆テレシネを判定中";changed();j.Telecine=await Telecine.Analyze(j,settings,token,log);changed();
   string subtitle=await Subtitles.Prepare(j,settings,dataFolder,token,log,changed);
   j.DelogoInput="";
   if(j.Preset.RemoveLogo)j.DelogoInput=await Delogo.Prepare(j,settings,dataFolder,token,log,changed);
   string delogoInput=j.DelogoInput.Length>0?j.DelogoInput:null;
   string cutFilter=null;
   if(keep!=null) {
    string folder=Commercials.Folder(j,dataFolder);Directory.CreateDirectory(folder);
    if(subtitle=="arib-raw")subtitle=await Subtitles.CutRawSubtitles(j,settings,folder,log,token);
    else subtitle=Commercials.CutSubtitles(j,subtitle,folder,log);
    // Delogo builds its intermediate already CM-trimmed (AVS Trim), so no trim filter is needed for it.
    if(delogoInput==null&&(j.Preset.Engine==Engine.CPU||j.Metadata.InverseTelecine)){cutFilter=Path.Combine(folder,"trim-filter.txt");File.WriteAllText(cutFilter,Commercials.Filter(keep,j.Metadata,Commands.Filters(j.Preset,j.Metadata)));}
   }
   string root=Path.GetFullPath(j.OutputFolder),relative=Names.Render(j.FileName,j.Metadata,j.Preset);
   string desired=Path.GetFullPath(Path.Combine(root,relative));if(!desired.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("出力先が保存フォルダーの範囲外です。");
   if(desired.Length>200)throw new ArgumentException("出力パスが長すぎます。マクロや保存先を短くしてください。");
   Directory.CreateDirectory(Path.GetDirectoryName(desired));
   string final=null,reserve=null,temp=null,selected=null,muxed=null,cutAudio=null,audioFilter=null,audioMux=null,delogoAudio=null,delogoAudioFilter=null;FileStream reservation=null;
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
     if(line.StartsWith("out_time_us=",StringComparison.Ordinal)&&j.Metadata.Duration>0)j.Progress=Math.Min(99,MetadataReader.Number(line.Substring(12))/1000000/expectedDuration*100);
    };
    Command command=null;
    bool advanced=AdvancedOptions.Enabled(p);
    if(advanced){j.Message="詳細パイプラインを開始中";changed();await AdvancedEncoder.Run(j,s,dataFolder,temp,token,log,changed);}
    else if(p.Engine==Engine.CPU)command=delogoInput==null?Commands.Ffmpeg(s,p,j.Metadata,j.MediaInput,temp,cutFilter:cutFilter):Commands.Ffmpeg(s,p,Delogo.VideoOnly(j.Metadata),delogoInput,temp);
    else {
     string encoder=p.Engine==Engine.NVEncC?s.Nvenc:p.Engine==Engine.VCEEncC?s.Vceenc:s.Qsvenc;if(!File.Exists(encoder))throw new FileNotFoundException("選択したGPUエンコーダーがありません。設定タブでパスを指定してください。",encoder);
     if(delogoInput!=null) {
      var dm=Delogo.VideoOnly(j.Metadata);
      if(j.Metadata.InverseTelecine||!string.IsNullOrWhiteSpace(p.Fps)) {
       selected=Path.Combine(EncodeCache.WorkFolder(dataFolder,s,j),"selected.mkv");
       j.Message="FPS・逆テレシネの可逆前処理中（空き容量が必要）";changed();
       var pre=Commands.Ffmpeg(s,p,dm,delogoInput,selected,true,false);
       log(pre.ToString());var preResult=await Processes.Run(pre.Exe,pre.Args,token,log);
       if(preResult.ExitCode!=0)throw new InvalidDataException("前処理失敗：終了コード"+preResult.ExitCode+"\n"+Tail(preResult.Error));
       command=Commands.Hardware(s,p,dm,selected,temp,true,null);
      }else command=Commands.Hardware(s,p,dm,delogoInput,temp,false,null);
     }
     else {
     bool prefiltered=!string.IsNullOrWhiteSpace(p.Fps)||j.Metadata.InverseTelecine;
     selected=Path.Combine(EncodeCache.WorkFolder(dataFolder,s,j),"selected"+(prefiltered?".mkv":".ts"));
     j.Message=prefiltered?"FPS・逆テレシネの可逆前処理中（空き容量が必要）":"対象サービスの映像・音声を分離中";changed();
     var demux=Commands.Ffmpeg(s,p,j.Metadata,j.MediaInput,selected,prefiltered,!prefiltered,cutFilter:cutFilter);
     log(demux.ToString());var result=await Processes.Run(demux.Exe,demux.Args,token,log);
     if(result.ExitCode!=0)throw new InvalidDataException("前処理失敗：終了コード"+result.ExitCode+"\n"+Tail(result.Error));
     command=Commands.Hardware(s,p,j.Metadata,selected,temp,prefiltered,j.Metadata.InverseTelecine?null:keep);
     }
    }
    if(!advanced){j.Command=command.ToString();j.Message="変換中："+p.Engine;log(j.Command);changed();
    var encoded=await Processes.Run(command.Exe,command.Args,token,progress);
    if(encoded.ExitCode!=0)throw new InvalidDataException("変換失敗：終了コード"+encoded.ExitCode+"\n"+Tail(encoded.Error));}
    if(!advanced&&keep!=null&&(p.Engine!=Engine.CPU||delogoInput!=null)&&(!j.Metadata.InverseTelecine||delogoInput!=null)&&j.Metadata.AudioIndex>=0) {
     j.Message="CM区間に合わせて音声を再構成中";changed();
     cutAudio=Path.Combine(Path.GetDirectoryName(final),".cut-audio-"+j.Id+".m4a");audioFilter=cutAudio+".filter";audioMux=Path.Combine(Path.GetDirectoryName(final),".cut-av-"+j.Id+Path.GetExtension(final));
     File.WriteAllText(audioFilter,Commercials.AudioFilter(keep,j.Metadata));
     var audio=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-y","-i",j.MediaInput,"-/filter_complex",audioFilter,"-map","[aout]","-c:a","aac","-b:a",p.AudioBitrate+"k",cutAudio},token,log);
     if(audio.ExitCode!=0)throw new InvalidDataException("CM音声の処理に失敗しました。");
     var combined=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-y","-i",temp,"-i",cutAudio,"-map","0:v:0","-map","1:a:0","-c","copy","-map_metadata","-1","-map_chapters","-1",audioMux},token,log);
     if(combined.ExitCode!=0)throw new InvalidDataException("CM映像と音声の結合に失敗しました。");File.Delete(temp);temp=audioMux;audioMux=null;
    }
    if(!advanced&&delogoInput!=null&&keep==null&&j.Metadata.AudioIndex>=0) {
     j.Message="ロゴ消し済み映像へ音声を結合中";changed();
     audioMux=Path.Combine(Path.GetDirectoryName(final),".delogo-av-"+j.Id+Path.GetExtension(final));
     if(p.CopyAudio) {
      // Realign the copied audio to the video timeline origin (same source-time seek the advanced mux uses).
      var copy=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-y","-i",temp,"-ss",Commercials.N(AdvancedEncoder.SourceStart(j.Metadata,0)),"-i",j.MediaInput,"-map","0:v:0","-map","1:"+j.Metadata.AudioIndex,"-c","copy","-map_metadata","-1","-map_chapters","-1",audioMux},token,log);
      if(copy.ExitCode!=0)throw new InvalidDataException("ロゴ消し映像と音声の結合に失敗しました。");File.Delete(temp);temp=audioMux;audioMux=null;
     }else {
      delogoAudio=Path.Combine(Path.GetDirectoryName(final),".delogo-audio-"+j.Id+".m4a");delogoAudioFilter=delogoAudio+".filter";
      File.WriteAllText(delogoAudioFilter,Commercials.AudioFilter(new List<TimeRange>{new TimeRange {Start=0,End=j.Metadata.Duration}},j.Metadata));
      var audio=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-y","-i",j.MediaInput,"-/filter_complex",delogoAudioFilter,"-map","[aout]","-c:a","aac","-b:a",p.AudioBitrate+"k",delogoAudio},token,log);
      if(audio.ExitCode!=0)throw new InvalidDataException("ロゴ消し用の音声処理に失敗しました。");
      var combined=await Processes.Run(s.Ffmpeg,new[]{"-hide_banner","-nostdin","-y","-i",temp,"-i",delogoAudio,"-map","0:v:0","-map","1:a:0","-c","copy","-map_metadata","-1","-map_chapters","-1",audioMux},token,log);
      if(combined.ExitCode!=0)throw new InvalidDataException("ロゴ消し映像と音声の結合に失敗しました。");File.Delete(temp);temp=audioMux;audioMux=null;
     }
    }
    if(p.EmbedMetadata||subtitle!=null||p.CmMode!=CmMode.Off||Subtitles.WantsRaw(j)) {
     j.Message="字幕・番組情報を動画に格納中";changed();muxed=Path.Combine(Path.GetDirectoryName(final),".mux-"+j.Id+Path.GetExtension(final));
     subtitle=await Subtitles.Pack(j,s,temp,subtitle,muxed,token,log);File.Delete(temp);temp=muxed;muxed=null;
    }
    token.ThrowIfCancellationRequested();if(!File.Exists(temp)||new FileInfo(temp).Length<1024)throw new InvalidDataException("出力が空です。");
    j.Message="出力ファイルを検証中";changed();
    var validate=await Processes.Run(s.Ffprobe,new[]{"-v","error","-show_streams","-show_format","-show_chapters","-of","json",temp},token,null,60);
    if(validate.ExitCode!=0)throw new InvalidDataException("出力を読み取れません。");
    var data=Store.Json.Deserialize<Dictionary<string,object>>(validate.Output);
    var video=MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).FirstOrDefault(x=>MetadataReader.Text(x,"codec_type")=="video");
    if(video==null||MetadataReader.Int(video,"width")==0)throw new InvalidDataException("出力に有効な映像がありません。");
    if(j.Metadata.AudioIndex>=0&&!MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).Any(x=>MetadataReader.Text(x,"codec_type")=="audio"))throw new InvalidDataException("出力から音声が失われています。");
    if(subtitle!=null&&!MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).Any(x=>MetadataReader.Text(x,"codec_type")=="subtitle"&&(!Subtitles.WantsRaw(j)||MetadataReader.Text(x,"codec_name")=="arib_caption"))) {
     if(!p.IgnoreSubtitleErrors)throw new InvalidDataException("出力から字幕が失われています。");
     Subtitles.Warning(j,"出力に字幕がないため、字幕なしとして保存します。",log);j.Metadata.SubtitleStatus="字幕なしで保存";subtitle=null;
    }
    if(advanced&&MetadataReader.Items(data,"streams").Select(MetadataReader.Obj).Count(x=>MetadataReader.Text(x,"codec_type")=="audio")!=AdvancedOptions.Audio(p,j.Metadata).Count)throw new InvalidDataException("出力音声トラック数が選択と一致しません。");
    if(p.BitDepth==10&&!MetadataReader.Text(video,"pix_fmt").Contains("10"))throw new InvalidDataException("10bit出力になっていません。");
    var expected=Commands.Resolution(p,j.Metadata);if(expected!=null&&(MetadataReader.Int(video,"width")!=expected[0]||MetadataReader.Int(video,"height")!=expected[1]))throw new InvalidDataException("出力の解像度が指定と一致しません。");
    double duration=MetadataReader.Number(MetadataReader.Text(MetadataReader.Obj(data.ContainsKey("format")?data["format"]:null),"duration"));
    if(expectedDuration>5&&(duration<expectedDuration*0.9-2||keep!=null&&Math.Abs(duration-expectedDuration)>Math.Max(2,1/j.Metadata.Fps)))throw new InvalidDataException("出力時間が入力より短すぎます。");
    if(j.Metadata.InverseTelecine&&Math.Abs(MetadataReader.Ratio(MetadataReader.Text(video,"avg_frame_rate"))-24000.0/1001)>0.05)throw new InvalidDataException("逆テレシネ出力が23.976fpsではありません。");
    if(Store.Fingerprint(j.Input)!=fingerprint)throw new IOException("処理中に入力ファイルが更新されました。出力を確定しません。");
    if(p.CmMode!=CmMode.Off&&!MetadataReader.Items(data,"chapters").Any())throw new InvalidDataException("出力にCM・本編チャプターがありません。");
    File.Move(temp,final);temp=null;j.Output=final;j.Progress=100;
    Store.Save(final+".encode.json",new {input=j.Input,output=final,preset=p,metadata=j.Metadata,command=j.Command,finished=DateTime.Now,commercials=j.Commercials,telecine=j.Telecine,advanced=j.Advanced,signal=j.Signal,timelineRepair=j.TimelineRepair,validation=MetadataReader.Obj(data.ContainsKey("format")?data["format"]:null)});
    Commercials.Export(j);
    j.CacheCleanupWarning="";
    try{EncodeCache.ClearForJob(dataFolder,j);log("完了したジョブのエンコード用キャッシュを削除しました。");}
    catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){j.CacheCleanupWarning=e.Message;log("完成動画は保存済みですが、キャッシュを削除できません："+e.Message);}
    j.Message=!string.IsNullOrEmpty(j.Metadata.SubtitleWarning)?"完了（字幕の警告あり・元TSを保持）":j.Preset.KeepSubtitles&&subtitle==null?"完了（"+j.Metadata.SubtitleStatus+"・元TSを保持）":"完了（元TSを保持）";if(j.CacheCleanupWarning.Length>0)j.Message+=" キャッシュ削除の警告あり";log(j.Message+"："+final);
   }finally {
    reservation?.Dispose();foreach(string ownFile in new[]{reserve,temp,selected,muxed,cutAudio,audioFilter,audioMux,delogoAudio,delogoAudioFilter})if(ownFile!=null&&File.Exists(ownFile))try{File.Delete(ownFile);}catch(IOException){}
   }
  }
  public static string Tail(string text)=>text.Length>1800?text.Substring(text.Length-1800):text;
 }
}
