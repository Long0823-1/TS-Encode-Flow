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
   string chapter=settings.ChapterExe,logoExe=settings.LogoFrame;
   if(!File.Exists(chapter)||!File.Exists(logoExe))throw new FileNotFoundException("設定でCM解析ツールとlogoframeをダウンロードして適用してください。");
   string bin=Path.GetDirectoryName(chapter),root=Path.GetDirectoryName(bin),jls=Path.Combine(bin,"join_logo_scp.exe"),plugin=Path.Combine(bin,"plugins64","LSMASHSource.dll"),script=string.IsNullOrWhiteSpace(settings.JoinLogoScript)?Path.Combine(root,"JL","JL_標準.txt"):settings.JoinLogoScript;
   foreach(string file in new[]{jls,plugin,script,Path.Combine(bin,"AviSynth.dll")})if(!File.Exists(file))throw new FileNotFoundException("CM解析の依存ファイルがありません。ツールを再ダウンロードしてください。",file);
   // logoframe loads its private AviSynth runtime, never a system-wide plugin folder.
   string runtime=Path.GetDirectoryName(logoExe);foreach(string dll in new[]{"AviSynth.dll","vcruntime140.dll","msvcp140.dll"}){string source=Path.Combine(bin,dll);if(File.Exists(source)&&!File.Exists(Path.Combine(runtime,dll)))File.Copy(source,Path.Combine(runtime,dll));}
   string work=Path.Combine(Commercials.Folder(job,data),"cm-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
   job.Message="元TSのフレーム時刻を確認中";changed();
   string cache=Path.Combine(Commercials.Folder(job,data),"frame-times.csv"),identity=cache+".identity";
   string identityValue=Store.Fingerprint(job.Input)+"|"+job.Metadata.VideoIndex;
   string frameText;
   if(File.Exists(cache)&&File.Exists(identity)&&File.ReadAllText(identity)==identityValue)frameText=File.ReadAllText(cache);
   else {
    var frames=await Processes.Run(settings.Ffprobe,new[]{"-v","error","-select_streams",job.Metadata.VideoIndex.ToString(),"-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0",job.Input},token);
    if(frames.ExitCode!=0)throw new InvalidDataException("元TSの時間軸を取得できません。");frameText=frames.Output;File.WriteAllText(cache,frameText);File.WriteAllText(identity,identityValue);
   }
   var times=frameText.Split('\n').Select(line=>line.Split(',')[0].Trim()).Where(x=>double.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out _)).Select(x=>double.Parse(x,CultureInfo.InvariantCulture)-job.Metadata.VideoStart).ToArray();
   bool protectTail=times.Length>1&&Math.Abs(times[^1]-times[^2]-1/job.Metadata.Fps)>.001;
   ValidateTimeline(times,times.Length,job.Metadata);
   job.Message="CM解析専用の映像を準備中（元TSを保持）";changed();string selected=Path.Combine(Commercials.Folder(job,data),"analysis.mkv");
   if(job.Metadata.AudioIndex<0)throw new InvalidDataException("無音検出に必要な音声ストリームがありません。");
   // A private proxy aligns both tools with FFmpeg's actually decodable frame sequence.
   // Broken leading GOPs can otherwise give L-SMASH and FFmpeg different frame counts.
   string audio="asetpts=PTS-STARTPTS+"+Commercials.N(job.Metadata.AudioStart-job.Metadata.VideoStart)+"/TB,aresample=async=1:first_pts=0";
   if(!File.Exists(selected)||!File.Exists(selected+".identity")||File.ReadAllText(selected+".identity")!=identityValue) {
   var selection=await Processes.Run(settings.Ffmpeg,new[]{"-hide_banner","-y","-i",job.Input,"-map","0:"+job.Metadata.VideoIndex,"-map","0:"+job.Metadata.AudioIndex,"-vf","setpts=PTS-STARTPTS","-af",audio,"-fps_mode","passthrough","-c:v","libx264","-preset","ultrafast","-crf","18","-threads","4","-pix_fmt","yuv420p","-c:a","pcm_s16le",selected},token,log);
   if(selection.ExitCode!=0)throw new InvalidDataException("CM解析用映像の準備に失敗しました。");File.WriteAllText(selected+".identity",identityValue);
   }
   string avs=Path.Combine(work,"source.avs");
   string code="ClearAutoloadDirs()\nLoadPlugin("+Avs(plugin)+")\nv=LWLibavVideoSource("+Avs(selected)+", repeat=false).AssumeFPS("+((int)Math.Round(job.Metadata.Fps*1000000)).ToString(CultureInfo.InvariantCulture)+",1000000)\na=LWLibavAudioSource("+Avs(selected)+")\nAudioDub(v,a).ConvertAudioTo16bit().ConvertToYV12()\n";
   Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);File.WriteAllText(avs,code,Encoding.GetEncoding(932,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback));
   var info=await Processes.Run(logoExe,new[]{"-info-only",avs},token,log);var match=Regex.Match(info.Output,@"CMINFO (\d+) (\d+) (\d+) (\d+) (\d+)");
   if(info.ExitCode!=0||!match.Success)throw new InvalidDataException("CM解析映像のフレーム基準を確認できません。");
   int width=int.Parse(match.Groups[1].Value),height=int.Parse(match.Groups[2].Value),num=int.Parse(match.Groups[3].Value),den=int.Parse(match.Groups[4].Value),count=int.Parse(match.Groups[5].Value);
   if(width!=job.Metadata.Width||height!=job.Metadata.Height||den<=0||Math.Abs((double)num/den-job.Metadata.Fps)>.001||count!=times.Length)throw new InvalidDataException("CM解析映像と元TSの形式・フレーム数が一致しません。");
   string chapters=Path.Combine(work,"chapter.txt"),logoOutput=Path.Combine(work,"logo.txt"),trim=Path.Combine(work,"trim.avs");
   if(!job.Preset.CmNoLogo) {
    job.Message="放送局ロゴを検出中（未登録局はロゴを自動生成）";changed();var logoArgs=new List<string>{avs,"-oa",logoOutput};
    if(!string.IsNullOrWhiteSpace(settings.CmLogo)){if(!File.Exists(settings.CmLogo))throw new FileNotFoundException("指定されたロゴLGDがありません。",settings.CmLogo);logoArgs.AddRange(new[]{"-logo",settings.CmLogo});}
    else {string logoCache=Path.Combine(data,"Logos");Directory.CreateDirectory(logoCache);logoArgs.AddRange(new[]{"-channel",job.Metadata.NetworkId+"-"+job.Metadata.TransportId+"-"+job.Metadata.ServiceId+"-"+width+"x"+height,"-logo-dir",logoCache});}
    var logo=await Processes.Run(logoExe,logoArgs,token,log);if(logo.ExitCode!=0||!File.Exists(logoOutput))throw new InvalidDataException("ロゴ検出に失敗しました。対象局のLGDを指定するか、プリセットで「ロゴを使わない」を明示的に選んでください。");
   }
   job.Message="chapter_exeで無音・シーンチェンジを検出中";changed();var chapterArgs=new List<string>{"-v",avs,"-o",chapters};if(job.Metadata.Fps>=50)chapterArgs.AddRange(new[]{"-s","20"});
   var detected=await Processes.Run(chapter,chapterArgs,token,log);if(detected.ExitCode!=0||!File.Exists(chapters))throw new InvalidDataException("無音・シーンチェンジ検出に失敗しました。");
   job.Message="join_logo_scpでCM区間を判定中";changed();var joinArgs=new List<string>{"-inscp",chapters,"-incmd",script,"-o",trim,"-oscp",Path.Combine(work,"timeline.txt")};if(!job.Preset.CmNoLogo)joinArgs.AddRange(new[]{"-inlogo",logoOutput});
   var joined=await Processes.Run(jls,joinArgs,token,log);if(joined.ExitCode!=0||!File.Exists(trim))throw new InvalidDataException("join_logo_scpのCM判定に失敗しました。");
   if(Store.Fingerprint(job.Input)+"|"+job.Metadata.VideoIndex!=identityValue)throw new IOException("CM解析中に入力TSが更新されました。再解析してください。");
   var plan=Commercials.Import(trim,job.Metadata,new Preset {CmTrimFps=job.Metadata.Fps.ToString("R",CultureInfo.InvariantCulture)},Store.Fingerprint(job.Input));plan.Source=job.Preset.CmNoLogo?"chapter_exe / join_logo_scp（ロゴなし）":"logoframe / chapter_exe / join_logo_scp";
   if(protectTail){var safe=plan.Segments.Where(x=>x.IsCommercial&&x.Start<plan.Duration-1).Select(x=>new TimeRange {Start=x.Start,End=Math.Min(x.End,plan.Duration-1)});plan=Commercials.Build(safe,plan.Duration,plan.Fps,plan.Fingerprint,plan.Source+"（末尾時刻差あり・最後の1秒を保持）");log("末尾フレームに時刻差があるため最後の1秒をCM削除から保護しました。");}
   plan.HasVideoStart=true;plan.SourceVideoStart=job.Metadata.VideoStart;File.Delete(selected);File.Delete(selected+".identity");return plan;
  }
  public static void ValidateTimeline(double[] times,int count,Metadata m) {
   if(!double.IsFinite(m.Fps)||m.Fps<=0)throw new InvalidDataException("入力FPSが不正です。");double step=1/m.Fps;if(count<1||times.Length!=count||!double.IsFinite(times[0])||times[0]<-.001||times[0]>2||Math.Abs(times[^1]+step-m.Duration)>step*2)throw new InvalidDataException($"元TSと解析映像のフレーム数・長さが一致しません（{times.Length}/{count}フレーム、先頭{(times.Length>0?times[0]:double.NaN):F3}秒、末尾{(times.Length>0?times[^1]+step:0):F3}/{m.Duration:F3}秒）。自動適用を停止しました。");
   for(int i=1;i<times.Length;i++)if(!double.IsFinite(times[i])||(Math.Abs(times[i]-times[i-1]-step)>.001&&!(i==times.Length-1&&times[i]-times[i-1]>=step&&times[i]-times[i-1]<=.5)))throw new InvalidDataException("VFR・RFF・ドロップ等で入力の時間軸が一定ではありません。自動適用せず、タイムリストで手動指定してください。");
   m.VideoStart+=times[0];m.Duration=Math.Round((times[^1]-times[0]+step)*m.Fps)/m.Fps;
  }
 }
}
