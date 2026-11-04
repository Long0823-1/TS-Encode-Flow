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
 public static class CmAnalysis {
  static string Avs(string path) {if(path.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new ArgumentException("AVSに使用できないパスです。");return "\""+Path.GetFullPath(path).Replace('\\','/')+"\"";}
  public static async Task<CmPlan> Analyze(Job job,Settings settings,string data,CancellationToken token,Action<string> log,Action changed) {
   token.ThrowIfCancellationRequested();StationLogos.Require(data,job,settings);
   string chapter=settings.ChapterExe,logoExe=settings.LogoFrame;
   string bundled=Path.Combine(AppContext.BaseDirectory,"Tools","CmLogo","logoframe.exe"),managed=Path.GetFullPath(Path.Combine(data,"Tools","logoframe"))+Path.DirectorySeparatorChar;
   if(File.Exists(bundled)&&(string.IsNullOrWhiteSpace(logoExe)||Path.GetFullPath(logoExe).StartsWith(managed,StringComparison.OrdinalIgnoreCase)))logoExe=bundled;
   if(!File.Exists(chapter)||!File.Exists(logoExe))throw new FileNotFoundException("設定でCM解析ツールとlogoframeをダウンロードして適用してください。");
   string bin=Path.GetDirectoryName(chapter),root=Path.GetDirectoryName(bin),jls=Path.Combine(bin,"join_logo_scp.exe"),plugin=Path.Combine(bin,"plugins64","LSMASHSource.dll"),script=string.IsNullOrWhiteSpace(settings.JoinLogoScript)?Path.Combine(root,"JL","JL_標準.txt"):settings.JoinLogoScript;
   foreach(string file in new[]{jls,plugin,script,Path.Combine(bin,"AviSynth.dll")})if(!File.Exists(file))throw new FileNotFoundException("CM解析の依存ファイルがありません。ツールを再ダウンロードしてください。",file);
   // logoframe loads its private AviSynth runtime, never a system-wide plugin folder.
   string runtime=Path.GetDirectoryName(logoExe);foreach(string dll in new[]{"AviSynth.dll","vcruntime140.dll","msvcp140.dll"}){string source=Path.Combine(bin,dll);if(File.Exists(source)&&!File.Exists(Path.Combine(runtime,dll)))File.Copy(source,Path.Combine(runtime,dll));}
   string work=Path.Combine(Commercials.Folder(job,data),"cm-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
   job.Message="元TSのフレーム時刻を確認中";changed();
   string cache=Path.Combine(Commercials.Folder(job,data),"frame-times.csv"),identity=cache+".identity";
   string identityValue=Store.Fingerprint(job.Input)+"|"+job.Metadata.VideoIndex;
   string frameIdentity=identityValue+"|"+Store.Fingerprint(settings.Ffprobe)+"|streamed-frames-v2";
   if(!File.Exists(cache)||!File.Exists(identity)||File.ReadAllText(identity)!=frameIdentity) {
    string pending=cache+"."+Guid.NewGuid().ToString("N")+".tmp";
    try {
     var frames=await Processes.RunToFile(settings.Ffprobe,new[]{"-v","error","-threads","4","-select_streams",job.Metadata.VideoIndex.ToString(),"-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",job.Input},pending,token);
     if(frames.ExitCode!=0)throw new InvalidDataException("元TSの時間軸を取得できません："+Converter.Tail(frames.Error));
     token.ThrowIfCancellationRequested();File.Move(pending,cache,true);File.WriteAllText(identity,frameIdentity);
    }finally{if(File.Exists(pending))File.Delete(pending);}
   }
   double headerVideoStart=job.Metadata.VideoStart;
   var times=ReadFrameTimes(cache,job.Metadata.VideoStart);
   bool protectTail=times.Length>1&&Math.Abs(times[^1]-times[^2]-1/job.Metadata.Fps)>.001;
   ValidateTimeline(times,times.Length,job.Metadata);
   if(job.Metadata.AudioIndex<0)throw new InvalidDataException("無音検出に必要な音声ストリームがありません。");
   Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
   var encoding=Encoding.GetEncoding(932,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
   string avs=Path.Combine(work,"source.avs"),privateIndex=Path.Combine(Commercials.Folder(job,data),"source-index.lwi");
   string sourceIdentity=Store.Fingerprint(job.Input)+"|"+Store.Fingerprint(plugin),privateIdentity=sourceIdentity+"|"+job.Metadata.VideoIndex+"|"+job.Metadata.AudioIndex;
   int nativeVideo=-1,nativeAudio=-1;
   string Script(string source,int first=0,int count=0) {
    string clip="AudioDub(v,a)"+(count>0?".Trim("+first+",-"+count+")":"");
    return "ClearAutoloadDirs()\nLoadPlugin("+Avs(plugin)+")\nv=LWLibavVideoSource("+Avs(source)+",stream_index="+nativeVideo+",threads=4,repeat=false)\na=LWLibavAudioSource("+Avs(source)+",stream_index="+nativeAudio+")\n"+clip+".AssumeFPS("+((int)Math.Round(job.Metadata.Fps*1000000)).ToString(CultureInfo.InvariantCulture)+",1000000).ConvertAudioTo16bit().ConvertToYV12()\n";
   }
   async Task<(int Width,int Height,int Count)> Info() {
    var info=await Processes.Run(logoExe,new[]{"-info-only",avs},token,log);var match=Regex.Match(info.Output,@"CMINFO (\d+) (\d+) (\d+) (\d+) (\d+)");
    if(info.ExitCode!=0||!match.Success)throw new InvalidDataException("CM解析ツールが映像・音声を開けません。詳細ログ："+(info.Error??info.Output).Trim());
    return(int.Parse(match.Groups[1].Value),int.Parse(match.Groups[2].Value),int.Parse(match.Groups[5].Value));
   }
   job.Message="元TSのCM解析インデックスを準備中（再エンコードなし）";changed();
   if(!File.Exists(privateIndex)||!File.Exists(privateIndex+".identity")||File.ReadAllText(privateIndex+".identity")!=privateIdentity) {
    using var lease=await CmIndex.Lock(job.Input,token);
    string sharedIndex=Path.GetFullPath(job.Input)+".lwi",cacheDir=Path.Combine(data,"Commercials","IndexCache");Directory.CreateDirectory(cacheDir);
    string canonical=Path.Combine(cacheDir,CmIndex.Key(job.Input)+".lwi"),receipt=canonical+".identity";
    if(!File.Exists(canonical)||!File.Exists(receipt)||File.ReadAllText(receipt)!=sourceIdentity) {
     string backup=Path.Combine(work,"previous-source-index.lwi");bool hadIndex=File.Exists(sharedIndex);if(hadIndex)File.Move(sharedIndex,backup);
     try {
      File.WriteAllText(avs,Script(job.Input),encoding);await Info();
      if(!File.Exists(sharedIndex))throw new InvalidDataException("CMインデックスを作成できません。録画フォルダーの書き込み権限を確認してください。");
      File.Copy(sharedIndex,canonical,true);File.WriteAllText(receipt,sourceIdentity);
     }finally {
      // This is the generated sidecar, never the source recording.
      if(File.Exists(sharedIndex))File.Delete(sharedIndex);
      if(hadIndex&&File.Exists(backup))File.Move(backup,sharedIndex);
     }
    }
    File.Copy(canonical,privateIndex,true);File.WriteAllText(privateIndex+".identity",privateIdentity);
   }
   async Task<(long Position,double Time)[]> Packets(int stream) {
    var probe=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-read_intervals","%+10","-select_streams",stream.ToString(),"-show_packets","-show_entries","packet=pos,pts_time","-of","json",job.Input},token);
    if(probe.ExitCode!=0)throw new InvalidDataException("CM解析のトラック照合用パケットを取得できません。");
    var json=Store.Json.Deserialize<Dictionary<string,object>>(probe.Output);
    return MetadataReader.Items(json,"packets").Select(MetadataReader.Obj).Select(p=>(long.TryParse(MetadataReader.Text(p,"pos"),out var pos)?pos:-1,double.TryParse(MetadataReader.Text(p,"pts_time"),NumberStyles.Float,CultureInfo.InvariantCulture,out var time)?time:double.NaN)).ToArray();
   }
   nativeVideo=CmIndex.MapStream(privateIndex,0,await Packets(job.Metadata.VideoIndex));nativeAudio=CmIndex.MapStream(privateIndex,1,await Packets(job.Metadata.AudioIndex));
   log("CMトラック照合：FFmpeg映像 "+job.Metadata.VideoIndex+" → 解析映像 "+nativeVideo+" / FFmpeg音声 "+job.Metadata.AudioIndex+" → 解析音声 "+nativeAudio);
   File.WriteAllText(avs,Script(privateIndex),encoding);var sourceInfo=await Info();
   if(sourceInfo.Width!=job.Metadata.Width||sourceInfo.Height!=job.Metadata.Height)throw new InvalidDataException("CM解析の映像形式が元TSと一致しません。");
   double[] indexed=CmIndex.Read(privateIndex,nativeVideo,sourceInfo.Count,out var widths,out var heights);
   int skip=CmIndex.Align(indexed,times.Select(t=>t+headerVideoStart).ToArray());
   for(int i=skip;i<skip+times.Length;i++)if(widths[i]!=job.Metadata.Width||heights[i]!=job.Metadata.Height)throw new InvalidDataException("解析区間の途中で映像解像度が変化しています。自動適用を停止しました。");
   log("元TSを直接解析：先頭調整 "+skip+" フレーム / 対応確認 "+times.Length+" フレーム / インデックス "+new FileInfo(privateIndex).Length+" bytes");
   File.WriteAllText(avs,Script(privateIndex,skip,times.Length),encoding);var aligned=await Info();
   if(aligned.Count!=times.Length)throw new InvalidDataException("CM解析のフレーム数を変換側へ一致させられません。");
   int width=sourceInfo.Width,height=sourceInfo.Height;
   string chapters=Path.Combine(work,"chapter.txt"),logoOutput=Path.Combine(work,"logo.txt"),trim=Path.Combine(work,"trim.avs");
   bool useLogo=await CmLogoFallback.UseLogo(job.Preset,string.IsNullOrWhiteSpace(settings.CmLogo),async()=> {
    using var stationLease=string.IsNullOrWhiteSpace(settings.CmLogo)&&StationLogos.Find(data,job)==null?await StationLogos.Lock(data,job,token):null;
    job.Message="放送局ロゴを検出中（登録済みロゴ・範囲を使用）";changed();var logoArgs=new List<string>{avs,"-oa",logoOutput};
    if(!string.IsNullOrWhiteSpace(settings.CmLogo)){if(!File.Exists(settings.CmLogo))throw new FileNotFoundException("指定されたロゴLGDがありません。",settings.CmLogo);logoArgs.AddRange(new[]{"-logo",settings.CmLogo});}
    else {
     string cached=StationLogos.Find(data,job);
     if(cached==null) {
      var area=StationLogos.Read(data,job).Area;if(area==null)throw new LogoAreaRequiredException();area.Validate(job.Metadata);
      string candidateData=Path.Combine(work,"LogoCandidate");Directory.CreateDirectory(Path.Combine(candidateData,"Logos"));
      log("指定範囲から局ロゴ候補を作成："+area);
      var generated=await Processes.Run(logoExe,new[]{avs,"-channel",StationLogos.Key(job),"-logo-dir",Path.Combine(candidateData,"Logos"),"-logo-area",area.ToString(),"-logo-generate-only"},token,log);
      token.ThrowIfCancellationRequested();if(generated.ExitCode!=0)return false;
      if(Store.Fingerprint(job.Input)+"|"+job.Metadata.VideoIndex!=identityValue)throw new IOException("ロゴ生成中に入力TSが変更されました。");
      job.LogoCandidate=StationLogos.Find(candidateData,job);if(job.LogoCandidate==null)return false;LogoCandidates.Read(job.LogoCandidate,job.Metadata);
      cached=StationLogos.Find(data,job);
      if(cached==null&&(generated.Output+generated.Error).Contains("CMLOGO_REVIEW_REQUIRED")){changed();throw new LogoReviewException();}
      if(cached==null)cached=LogoCandidates.Promote(data,job);job.LogoCandidate="";
     }
     log("保存済み局ロゴ："+cached);logoArgs.AddRange(new[]{"-logo",cached});
    }
    var logo=await Processes.Run(logoExe,logoArgs,token,log);return logo.ExitCode==0&&File.Exists(logoOutput)&&new FileInfo(logoOutput).Length>0;
   },token,log);
   job.Message="chapter_exeで無音・シーンチェンジを検出中";changed();var chapterArgs=new List<string>{"-v",avs,"-o",chapters};if(job.Metadata.Fps>=50)chapterArgs.AddRange(new[]{"-s","20"});
   var detected=await Processes.Run(chapter,chapterArgs,token,log);if(detected.ExitCode!=0||!File.Exists(chapters))throw new InvalidDataException("無音・シーンチェンジ検出に失敗しました。");
   job.Message="join_logo_scpでCM区間を判定中";changed();var joinArgs=new List<string>{"-inscp",chapters,"-incmd",script,"-o",trim,"-oscp",Path.Combine(work,"timeline.txt")};if(useLogo)joinArgs.AddRange(new[]{"-inlogo",logoOutput});
   var joined=await Processes.Run(jls,joinArgs,token,log);if(joined.ExitCode!=0||!File.Exists(trim))throw new InvalidDataException("join_logo_scpのCM判定に失敗しました。");
   if(Store.Fingerprint(job.Input)+"|"+job.Metadata.VideoIndex!=identityValue)throw new IOException("CM解析中に入力TSが更新されました。再解析してください。");
   var plan=Commercials.Import(trim,job.Metadata,new Preset {CmTrimFps=job.Metadata.Fps.ToString("R",CultureInfo.InvariantCulture)},Store.Fingerprint(job.Input));plan.Source=useLogo?"logoframe / chapter_exe / join_logo_scp":job.Preset.CmNoLogo?"chapter_exe / join_logo_scp（ロゴなし）":"chapter_exe / join_logo_scp（自動ロゴ取得失敗によりロゴなしへ切替）";
   if(protectTail){var safe=plan.Segments.Where(x=>x.IsCommercial&&x.Start<plan.Duration-1).Select(x=>new TimeRange {Start=x.Start,End=Math.Min(x.End,plan.Duration-1)});plan=Commercials.Build(safe,plan.Duration,plan.Fps,plan.Fingerprint,plan.Source+"（末尾時刻差あり・最後の1秒を保持）");log("末尾フレームに時刻差があるため最後の1秒をCM削除から保護しました。");}
   plan.HasVideoStart=true;plan.SourceVideoStart=job.Metadata.VideoStart;
   File.Delete(privateIndex);File.Delete(privateIndex+".identity");
   string legacy=Path.Combine(Commercials.Folder(job,data),"analysis.mkv");if(File.Exists(legacy)&&File.Exists(legacy+".identity")&&File.ReadAllText(legacy+".identity")==identityValue){File.Delete(legacy);File.Delete(legacy+".identity");}
   return plan;
  }
  public static void ValidateTimeline(double[] times,int count,Metadata m) {
   if(!double.IsFinite(m.Fps)||m.Fps<=0)throw new InvalidDataException("入力FPSが不正です。");double step=1/m.Fps;
   if(count<1||times.Length!=count||!double.IsFinite(times[0])||times[0]<-.001||times[0]>2)throw new InvalidDataException("入力TSのフレーム時刻を正しく読み取れません。自動CM適用を停止しました。");
   var gaps=new List<(int Index,double Delta)>();
   for(int i=1;i<times.Length;i++) {
    if(!double.IsFinite(times[i])||times[i]<=times[i-1])throw new InvalidDataException("入力TSのフレーム時刻が重複または逆行しています。自動CM適用を停止しました。");
    double delta=times[i]-times[i-1];
    if(Math.Abs(delta-step)>.001&&!(i==times.Length-1&&delta>=step&&delta<=.5))gaps.Add((i,delta));
   }
   if(gaps.Count>0) {
    var largest=gaps.OrderByDescending(x=>x.Delta).First();double missing=gaps.Sum(x=>Math.Max(0,x.Delta-step));
    throw new InvalidDataException($"入力TSの時間軸が不連続です（{gaps.Count}か所、時刻差の合計{missing:F3}秒）。最大の飛び：{Commercials.Time(times[largest.Index-1]-times[0])} → {Commercials.Time(times[largest.Index]-times[0])}。録画の欠落・ドロップやVFR/RFFを確認してください。CM位置と音声のずれを防ぐため、自動適用を停止しました。");
   }
   if(!double.IsFinite(m.Duration)||Math.Abs(times[^1]+step-m.Duration)>step*2)throw new InvalidDataException($"入力TSのフレーム時刻と録画時間が一致しません（{times.Length}フレーム、先頭{times[0]:F3}秒、末尾{times[^1]+step:F3}/{m.Duration:F3}秒）。自動CM適用を停止しました。");
   m.VideoStart+=times[0];m.Duration=Math.Round((times[^1]-times[0]+step)*m.Fps)/m.Fps;
  }
  public static double[] ReadFrameTimes(string file,double start)=>File.ReadLines(file).Select(line=>line.Split(',')[0].Trim()).Where(x=>double.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out _)).Select(x=>double.Parse(x,CultureInfo.InvariantCulture)-start).ToArray();
 }
}
