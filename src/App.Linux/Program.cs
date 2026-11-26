using Avalonia;
using Avalonia.Headless;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

static class Program {
 [STAThread]
 public static int Main(string[] args) {
  // 診断モード（GUIを起動せず、GUIと同じクライアント経路だけを検証する）
  int smoke=Array.IndexOf(args,"--smoke-enqueue");
  if(smoke>=0)return SmokeEnqueue(args,smoke);
  int b2a=Array.IndexOf(args,"--smoke-b2a");
  if(b2a>=0)return SmokeB2a(args,b2a);
  int b2b=Array.IndexOf(args,"--smoke-b2b");
  if(b2b>=0)return SmokeB2b(args,b2b);
  int shot=Array.IndexOf(args,"--screenshot");
  if(shot>=0&&shot+1<args.Length)return Screenshot(args,args[shot+1]);
  BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
  return 0;
 }

 public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

 // --smoke-enqueue <TSファイル> [エンジン名]
 // EngineClientLinux.Add（＝GUIの「TSを追加」と同じ経路）で登録し、ワーカーの処理結果を queue.json から読み戻す。
 static int SmokeEnqueue(string[] args,int index) {
  if(index+1>=args.Length){Console.Error.WriteLine("TSファイルを指定してください。");return 2;}
  string file=Path.GetFullPath(args[index+1]);
  string engine=index+2<args.Length?args[index+2]:"";
  try {
   var client=new EngineClientLinux();
   if(engine.Length>0) {
    var preset=client.Settings.Presets.FirstOrDefault(p=>PresetCatalog.EngineName(p.Engine)==engine&&p.Codec==Codec.H264)
     ??client.Settings.Presets.FirstOrDefault(p=>PresetCatalog.EngineName(p.Engine)==engine);
    if(preset==null)throw new ArgumentException("そのエンジンのプリセットがありません："+engine);
    client.Choose(preset);
   }
   Console.WriteLine("データフォルダー："+client.Data);
   Console.WriteLine("使用プリセット："+client.Selected.Name+"（"+PresetCatalog.EngineName(client.Selected.Engine)+" · "+client.Selected.Codec+" · "+client.Selected.Container+"）");
   int added=client.Add([file]);
   Console.WriteLine(added+"本を登録しました。ワーカーの処理を待ちます…");
   for(int i=0;i<180;i++) {
    Thread.Sleep(1000);client.Refresh();
    var job=client.Jobs.FirstOrDefault(j=>j.Input==file);
    if(job==null)continue;
    Console.WriteLine("状態："+job.State+" "+job.Progress.ToString("0")+"% "+job.Message);
    if(job.State==State.Completed){Console.WriteLine("出力："+job.Output);return 0;}
    if(job.State==State.Failed){Console.Error.WriteLine("失敗："+job.Message);return 1;}
   }
   Console.Error.WriteLine("タイムアウト：変換の完了を確認できませんでした。");
   return 3;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }

 // --smoke-b2a <TSファイル> : B-2aの検証。プリセット編集モデル→SavePreset、設定画面の保存経路、
 // 自動化画面の保存経路を実際に通し、最後に登録したジョブへワーカーが反映するかを確認する。
 static int SmokeB2a(string[] args,int index) {
  if(index+1>=args.Length){Console.Error.WriteLine("TSファイルを指定してください。");return 2;}
  string file=Path.GetFullPath(args[index+1]);
  try {
   var client=new EngineClientLinux();
   Console.WriteLine("データフォルダー："+client.Data);
   // 1) プリセット：編集モデル経由で VA-API のカスタムプリセットを作成・保存（選択中になる）
   var model=new PresetEditorModel(client.Selected,true) {Name="B2aスモーク用"};
   model.EngineIndex=(int)Engine.Vaapi;model.CodecIndex=(int)Codec.H264;
   client.SavePreset(model.Apply());
   var saved=client.Settings.Presets.FirstOrDefault(p=>p.Name=="B2aスモーク用"&&string.IsNullOrEmpty(p.FactoryId));
   if(saved==null)throw new InvalidOperationException("カスタムプリセットが保存されていません。");
   if(!ReferenceEquals(client.Selected,saved))throw new InvalidOperationException("保存したプリセットが選択中になっていません。");
   Console.WriteLine("プリセット保存OK："+saved.Name+"（"+PresetCatalog.EngineName(saved.Engine)+" · "+saved.Codec+"）");
   // 2) 設定：設定画面の保存経路で出力先と同時変換数を変更し、settings.json へ反映されるか
   string outDir=Path.Combine(client.Data,"smoke-output");
   var values=SettingsValues.Capture(client.Settings);values.OutputFolder=outDir;values.Parallel=2;
   SettingsScreenLogic.ApplySettings(client,values);
   var disk=Integration.LoadSettings(client.Data);
   if(Path.GetFullPath(disk.OutputFolder)!=Path.GetFullPath(outDir))throw new InvalidOperationException("保存先が settings.json に反映されていません。");
   if(disk.Parallel!=2)throw new InvalidOperationException("同時変換数が settings.json に反映されていません。");
   Console.WriteLine("設定保存OK：出力先="+disk.OutputFolder+" / 同時変換数="+disk.Parallel);
   // 3) 自動化：自動化画面の保存経路（検証→保存→Wake）を実際に通す
   var automation=AutomationValues.Capture(client.Settings);
   AutomationScreenLogic.ApplyAutomation(client,automation);
   Console.WriteLine("自動化保存OK：監視="+(automation.WatchEnabled?"有効":"無効")+" / 同時変換数="+Integration.LoadSettings(client.Data).Parallel);
   // 4) 登録：ワーカーがカスタムプリセットと新しい出力先で処理するか
   int added=client.Add([file]);
   Console.WriteLine(added+"本を登録しました。ワーカーの処理を待ちます…");
   for(int i=0;i<240;i++) {
    Thread.Sleep(1000);client.Refresh();
    var job=client.Jobs.FirstOrDefault(j=>j.Input==file);
    if(job==null)continue;
    if(job.State==State.Completed) {
     if(job.Preset.Name!="B2aスモーク用")throw new InvalidOperationException("ジョブのプリセットがカスタムプリセットではありません："+job.Preset.Name);
     if(!Path.GetFullPath(job.Output).StartsWith(Path.GetFullPath(outDir)))throw new InvalidOperationException("ジョブの出力先が新しい保存先ではありません："+job.Output);
     Console.WriteLine("変換完了："+job.Output+"（プリセット："+job.Preset.Name+"）");
     Console.WriteLine("SMOKE-B2A OK");
     return 0;
    }
    if(job.State==State.Failed){Console.Error.WriteLine("失敗："+job.Message);return 1;}
   }
   Console.Error.WriteLine("タイムアウト：変換の完了を確認できませんでした。");
   return 3;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }

 // --screenshot <PNGパス> [--section <セクション名>] [--edit-preset] : ヘッドレスで描画して保存する（検証用）
 // [--fixture-b2b <TSファイル>] : B-2b各画面の表示用フィクスチャ（字形・CM区間・ロゴ）をデータフォルダーへ用意する
 // [--demo-signal <TSファイル>] : 解析・比較画面にTS診断（トランスポート解析）の結果を表示する
 static int Screenshot(string[] args,string output) {
  output=Path.GetFullPath(output);
  AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
  var client=new EngineClientLinux();
  int fx=Array.IndexOf(args,"--fixture-b2b");
  if(fx>=0&&fx+1<args.Length)PrepareFixtureB2b(client,Path.GetFullPath(args[fx+1]));
  var viewModel=new MainViewModel(client);
  int si=Array.IndexOf(args,"--section");
  if(si>=0&&si+1<args.Length)viewModel.Section=args[si+1];
  if(Array.IndexOf(args,"--edit-preset")>=0)viewModel.OpenPresetEditorDemo();
  int ds=Array.IndexOf(args,"--demo-signal");
  if(ds>=0&&ds+1<args.Length) {
   string ts=Path.GetFullPath(args[ds+1]);
   viewModel.Section="解析・比較";
   viewModel.LabScreen.LoadSignalDemo(ts,ProbeDuration(client.Settings.Ffprobe,ts));
  }
  var window=new MainWindow {DataContext=viewModel,Width=1180,Height=760};
  window.Show();
  // 自動起動したワーカーの状態書き込みを待ってから撮る
  for(int i=0;i<4;i++){AvaloniaHeadlessPlatform.ForceRenderTimerTick();Thread.Sleep(400);viewModel.Refresh();}
  AvaloniaHeadlessPlatform.ForceRenderTimerTick();
  var frame=window.CaptureRenderedFrame();
  if(frame==null){Console.Error.WriteLine("フレームを取得できませんでした。");return 1;}
  Directory.CreateDirectory(Path.GetDirectoryName(output)!);
  frame.Save(output);
  Console.WriteLine("スクリーンショットを保存しました："+output);
  return 0;
 }

 // ===== B-2b 検証用ユーティリティ =====

 // QueueManager.Add が登録時に設定するフィールドを、直接投入するフィクスチャジョブにも揃える。
 static void InitFixtureJob(EngineClientLinux client,Job job) {
  job.LogFile=Path.Combine(client.Data,"Logs",job.Id+".log");
  job.OutputFolder=Path.GetFullPath(client.Settings.OutputFolder);
  job.FileName=string.IsNullOrWhiteSpace(job.Preset.FileName)?client.Settings.FileName:job.Preset.FileName;
  if(string.IsNullOrEmpty(job.PresetSource))job.PresetSource="manual";
 }

 // CaptionProbe の配置候補（設定で検出済み → 配置済みWorker → 開発ツリー → PATH の順）。
 static string FindCaptionProbe(EngineClientLinux client) {
  if(File.Exists(client.Settings.CaptionTool))return client.Settings.CaptionTool;
  var candidates=new List<string> {Path.Combine(AppContext.BaseDirectory,"Worker","Tools","CaptionProbe")};
  var dir=new DirectoryInfo(AppContext.BaseDirectory);
  for(int i=0;i<8&&dir!=null;i++,dir=dir.Parent) {
   candidates.Add(Path.Combine(dir.FullName,"src","Worker","bin","Release","net9.0-windows","Tools","CaptionProbe"));
   candidates.Add(Path.Combine(dir.FullName,"src","Worker","bin","Debug","net9.0-windows","Tools","CaptionProbe"));
   candidates.Add(Path.Combine(dir.FullName,"Worker","bin","Release","net9.0-windows","Tools","CaptionProbe"));
  }
  candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"TS-Encode-Flow","src","Worker","bin","Debug","net9.0-windows","Tools","CaptionProbe"));
  candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"TS-Encode-Flow","src","Worker","bin","Release","net9.0-windows","Tools","CaptionProbe"));
  foreach(string path in candidates)if(File.Exists(path))return path;
  foreach(string root in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) {
   string path=Path.Combine(root,"CaptionProbe");if(File.Exists(path))return path;
  }
  return "";
 }

 // CaptionProbe のセルフテスト（内蔵ARIBサンプル）を実行し、字形一覧を得る。画像パスは絶対パスに直す。
 static List<DrcsGlyph> SelfTestGlyphs(EngineClientLinux client,string scanFolder) {
  string tool=FindCaptionProbe(client);
  if(tool.Length==0)throw new FileNotFoundException("CaptionProbe が見つかりません。scripts/build-native.sh でビルドしてください。");
  Directory.CreateDirectory(scanFolder);
  // スクリーンショット経路では Avalonia の同期コンテキストが入っているため、継続が戻れず固まらないよう別スレッドで実行する
  var run=Task.Run(async()=>await Processes.Run(tool,new[]{"--self-test",scanFolder},CancellationToken.None)).GetAwaiter().GetResult();
  if(run.ExitCode!=0)throw new InvalidDataException("CaptionProbe のセルフテストに失敗しました："+run.Error);
  var scan=Store.Load<CaptionScan>(Path.Combine(scanFolder,"captions.json"),()=>throw new InvalidDataException("字幕解析結果がありません。"));
  foreach(var glyph in scan.Glyphs)if(!Path.IsPathRooted(glyph.Image))glyph.Image=Path.Combine(scanFolder,glyph.Image);
  return scan.Glyphs;
 }

 static Metadata ReadMetadata(EngineClientLinux client,string file)=>
  Task.Run(()=>MetadataReader.Read(file,client.Settings,0,CancellationToken.None,_=>{})).GetAwaiter().GetResult();

 static double ProbeDuration(string ffprobe,string file) {
  try {
   var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffprobe,"-v error -show_entries format=duration -of csv=p=0 "+Processes.Quote(file)){UseShellExecute=false,RedirectStandardOutput=true})!;
   string text=p.StandardOutput.ReadToEnd();p.WaitForExit(30000);
   return double.TryParse(text.Trim(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double d)?d:0;
  }catch{return 0;}
 }

 // LGD v0.1 形式のフィクスチャ（LogoCandidates.Read が検証する実フォーマットで生成する）。
 static void WriteFixtureLgd(string path,int x,int y,int width,int height) {
  using var stream=new FileStream(path,FileMode.Create,FileAccess.Write);using var w=new BinaryWriter(stream);
  var signature=new byte[28];System.Text.Encoding.ASCII.GetBytes("<logo data file ver0.1>").CopyTo(signature,0);w.Write(signature);
  w.Write(new byte[]{0,0,0,1});w.Write(new byte[32]);
  w.Write((short)x);w.Write((short)y);w.Write((short)height);w.Write((short)width);w.Write(new byte[8]);
  for(int i=0;i<width*height;i++){w.Write((short)900);w.Write((short)3800);w.Write(new byte[8]);}
 }

 // スクリーンショット用のフィクスチャ：字形ジョブ・CM確認待ちジョブ・ロゴ範囲待ちジョブ・登録済み局を並べる。
 static void PrepareFixtureB2b(EngineClientLinux client,string file) {
  var metadata=ReadMetadata(client,file);
  string fingerprint=Store.Fingerprint(file);
  var jobs=new List<Job>();
  // 字幕・DRCS：セルフテストの字形を持つ入力待ちジョブ
  try {
   var glyphs=SelfTestGlyphs(client,Path.Combine(client.Data,"Drcs","Scans","selftest"));
   var meta=Store.Clone(metadata);meta.Title="DRCSフィクスチャ";meta.SubtitleCodec="arib_caption";
   jobs.Add(new Job {Input=file,Fingerprint=fingerprint,State=State.NeedsInput,Preset=Store.Clone(client.Selected),Metadata=meta,Glyphs=glyphs,Message="DRCSの代替文字を確認してください"});
  }catch(Exception e){Console.Error.WriteLine("DRCSフィクスチャを用意できません："+e.Message);}
  // CM：確認待ちジョブ（10–20秒をCM候補）
  var cmPreset=Store.Clone(client.Selected);cmPreset.CmMode=CmMode.Cut;cmPreset.CmReview=true;cmPreset.KeepSubtitles=false;
  var cmMeta=Store.Clone(metadata);cmMeta.Title="CMフィクスチャ";
  var plan=Commercials.Build(new[]{new TimeRange {Start=10,End=20}},metadata.Duration,metadata.Fps,fingerprint,"スモーク用手動");
  jobs.Add(new Job {Input=file,Fingerprint=fingerprint,State=State.NeedsCmReview,Preset=cmPreset,Metadata=cmMeta,Commercials=plan,Message="CM候補を確認してください"});
  // 放送局ロゴ：範囲指定待ちジョブ
  var logoPreset=Store.Clone(client.Selected);logoPreset.CmMode=CmMode.Chapters;logoPreset.CmSource=CmSource.Amatsukaze;logoPreset.CmNoLogo=false;logoPreset.CmReview=true;
  var logoMeta=Store.Clone(metadata);logoMeta.Title="ロゴフィクスチャ";
  jobs.Add(new Job {Input=file,Fingerprint=fingerprint,State=State.NeedsLogo,Preset=logoPreset,Metadata=logoMeta,Message="局ロゴの範囲を指定してください"});
  // 登録済み局（LGD＋範囲）：ロゴ画面の一覧とプレビュー用
  string key="9999-8888-7777-1280x720";
  string logos=Path.Combine(client.Data,"Logos");Directory.CreateDirectory(logos);
  string lgd=Path.Combine(logos,key+"-v0001.lgd");WriteFixtureLgd(lgd,1100,40,120,40);
  File.WriteAllText(Path.Combine(logos,key+".latest"),"1\n"+key+"-v0001.lgd\n");
  Store.Save(Path.Combine(logos,key+".area.json"),new StationLogo {Key=key,Channel="テスト局",Width=1280,Height=720,Area=new LogoArea {X=1100,Y=40,Width=120,Height=40}});
  foreach(var j in jobs)InitFixtureJob(client,j);
  Store.Save(Path.Combine(client.Data,"queue.json"),jobs);
  client.Refresh();
  Console.WriteLine("B-2bフィクスチャを用意しました（ジョブ "+jobs.Count+" 本）");
 }

 // --smoke-b2b <TSファイル> : B-2bの検証。各画面のロジック経路を実際に通し、ワーカー経由の反映まで確認する。
 static int SmokeB2b(string[] args,int index) {
  if(index+1>=args.Length){Console.Error.WriteLine("TSファイルを指定してください。");return 2;}
  string file=Path.GetFullPath(args[index+1]);
  try {
   var client=new EngineClientLinux();
   Console.WriteLine("データフォルダー："+client.Data);
   var metadata=ReadMetadata(client,file);
   string fingerprint=Store.Fingerprint(file);
   Console.WriteLine("入力情報："+metadata.Width+" × "+metadata.Height+" / "+metadata.Fps.ToString("F3")+"fps / "+metadata.Duration.ToString("F2")+"秒 / 音声 "+metadata.AudioTracks.Count+" 本");

   // --- フィクスチャ投入（queue.json へ直接。ワーカーはこの後の Wake で処理する） ---
   var glyphs=SelfTestGlyphs(client,Path.Combine(client.Data,"Drcs","Scans","selftest"));
   if(glyphs.Count==0)throw new InvalidOperationException("セルフテストの字形がありません。");
   var jobs=new List<Job>();
   var metaA=Store.Clone(metadata);metaA.Title="DRCSスモーク";metaA.SubtitleCodec="arib_caption";
   var jobA=new Job {Input=file,Fingerprint=fingerprint,State=State.NeedsInput,Preset=Store.Clone(client.Selected),Metadata=metaA,Glyphs=Store.Clone(glyphs),Message="DRCSの代替文字を確認してください"};
   jobs.Add(jobA);
   var cmPreset=Store.Clone(client.Selected);cmPreset.Engine=Engine.CPU;cmPreset.CmMode=CmMode.Cut;cmPreset.CmReview=true;cmPreset.KeepSubtitles=false;
   var metaB=Store.Clone(metadata);metaB.Title="CMスモーク";
   var planB=Commercials.Build(new[]{new TimeRange {Start=10,End=20}},metadata.Duration,metadata.Fps,fingerprint,"スモーク用手動");
   var jobB=new Job {Input=file,Fingerprint=fingerprint,State=State.NeedsCmReview,Preset=cmPreset,Metadata=metaB,Commercials=planB,Message="CM候補を確認してください"};
   jobs.Add(jobB);
   var logoPreset=Store.Clone(client.Selected);logoPreset.CmMode=CmMode.Chapters;logoPreset.CmSource=CmSource.Amatsukaze;logoPreset.CmNoLogo=false;logoPreset.CmReview=true;
   var metaC=Store.Clone(metadata);metaC.Title="ロゴスモーク";
   var jobC=new Job {Input=file,Fingerprint=fingerprint,State=State.NeedsLogo,Preset=logoPreset,Metadata=metaC,Message="局ロゴの範囲を指定してください"};
   jobs.Add(jobC);
   foreach(var j in jobs)InitFixtureJob(client,j);
   Store.Save(Path.Combine(client.Data,"queue.json"),jobs);
   client.Refresh();

   // --- 字幕・DRCS：収集（読み取りのみ。保存と再開依頼は最後に行う） ---
   var (map,collected)=DrcsScreenLogic.Collect(client);
   if(collected.Count!=glyphs.Count)throw new InvalidOperationException("DRCS字形の収集結果が一致しません："+collected.Count+" / "+glyphs.Count);
   Console.WriteLine("DRCS収集OK：字形 "+collected.Count+" 件（未対応 "+collected.Count(g=>!Drcs.Resolved(g,map))+" 件）");

   // --- CM：画面の行データ→確定（approve-cm）→ワーカーが変換まで完走するか ---
   var available=CommercialScreenLogic.Available(client);
   var cmJob=available.FirstOrDefault(j=>j.Id==jobB.Id)??throw new InvalidOperationException("CM確認待ちジョブが画面の一覧にありません。");
   if(cmJob.Commercials!.Segments.Count(s=>s.IsCommercial)!=1)throw new InvalidOperationException("CM区間の行データが不正です。");
   client.Wake();
   CommercialScreenLogic.Approve(client,cmJob,new List<TimeRange>{new(){Start=10,End=20}},CmMode.Cut);
   Console.WriteLine("CM確定を送信しました。ワーカーの処理を待ちます…");
   bool cmDone=false;
   for(int i=0;i<300;i++) {
    Thread.Sleep(1000);client.Refresh();
    var current=client.Jobs.FirstOrDefault(j=>j.Id==jobB.Id);if(current==null)continue;
    if(current.State==State.Completed) {
     if(current.Commercials?.Approved!=true)throw new InvalidOperationException("確定済みのはずのCMプランが未承認です。");
     double duration=ProbeDuration(client.Settings.Ffprobe,current.Output);
     Console.WriteLine("CM変換完了："+current.Output+"（出力 "+duration.ToString("F2")+" 秒 / CM 10 秒をカット）");
     if(duration<15||duration>25)throw new InvalidOperationException("CMカット後の長さが想定と違います："+duration.ToString("F2")+" 秒");
     cmDone=true;break;
    }
    if(current.State==State.Failed)throw new InvalidOperationException("CMジョブが失敗しました："+current.Message);
   }
   if(!cmDone)throw new TimeoutException("CMジョブの完了を確認できませんでした。");

   // --- 放送局ロゴ：行データ→範囲指定（set-logo-area）→ワーカー適用 ---
   var (_,rowsBefore)=StationLogosScreenLogic.BuildRows(client);
   if(!rowsBefore.Any(r=>r.Configure?.Id==jobC.Id))throw new InvalidOperationException("ロゴ範囲を指定できる行がありません。");
   Console.WriteLine("ロゴ行データOK：範囲指定待ちの行を確認");
   var area=new LogoArea {X=1150,Y=30,Width=100,Height=50};
   string areaRequest=Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json");
   Store.Save(areaRequest,new QueueManager.Request {Action="set-logo-area",JobId=jobC.Id,Fingerprint=fingerprint,LogoArea=area});
   client.Wake();
   bool areaSaved=false;
   for(int i=0;i<60;i++) {
    Thread.Sleep(500);
    var savedArea=StationLogos.Read(client.Data,jobC).Area;
    if(savedArea!=null&&savedArea.ToString()==area.ToString()){areaSaved=true;break;}
    if(File.Exists(areaRequest+".error"))throw new IOException("ロゴ範囲を適用できませんでした。");
   }
   if(!areaSaved)throw new TimeoutException("ロゴ範囲の保存を確認できませんでした。");
   Console.WriteLine("ロゴ範囲保存OK："+area+"（StationLogos の area.json へ反映）");

   // --- 放送局ロゴ：LGD手動追加（ManageLogo）→ Delogo のロゴ解決 ---
   string lgdPath=Path.Combine(client.Data,"fixture.lgd");WriteFixtureLgd(lgdPath,1100,40,120,40);
   var station=new StationLogo {Key="9999-8888-7777-1280x720",Channel="スモーク局",Width=1280,Height=720};
   var import=client.ManageLogo(new QueueManager.Request {Action="import-logo",LogoStation=station,LogoSource=lgdPath,LogoSourceFingerprint=Store.Fingerprint(lgdPath)}).GetAwaiter().GetResult();
   string found=StationLogos.Find(client.Data,station.Key)??throw new InvalidOperationException("追加したLGDが見つかりません。");
   Console.WriteLine("LGD追加OK："+import.Message+"（"+Path.GetFileName(found)+"）");
   var delogoJob=new Job {Input=file,Metadata=new Metadata {NetworkId=9999,TransportId=8888,ServiceId=7777,Width=1280,Height=720}};
   string resolved=Delogo.ResolveLogo(client.Data,delogoJob,client.Settings);
   if(!string.Equals(resolved,found,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Delogo のロゴ解決先が追加したLGDと一致しません。");
   Console.WriteLine("Delogo解決OK："+Path.GetFileName(resolved));
   var (_,rowsAfter)=StationLogosScreenLogic.BuildRows(client);
   if(!rowsAfter.Any(r=>r.Station.Key==station.Key&&r.Current))throw new InvalidOperationException("追加した局が「使用中」で一覧に出ません。");
   Console.WriteLine("ロゴ一覧OK：追加した局が使用中として表示されます");

   // --- 解析・比較：TS診断（Core の SignalDiagnostics）を画面の経路で実行 ---
   var (report,signalMeta,_)=LabScreenLogic.Signal(client,file,Store.Clone(client.Selected),CancellationToken.None,m=>{}).GetAwaiter().GetResult();
   if(report.Packets<=0)throw new InvalidOperationException("TS診断のパケット数が 0 です。");
   Console.WriteLine("TS診断OK：パケット "+report.Packets+" / CC欠落候補 "+report.ContinuityErrors+" / TEI "+report.TransportErrors+" / 同期ずれ "+report.SyncErrors+" / 時刻飛び "+report.TimestampGaps+"（signal.json 保存済み）");
   // --- 字幕・DRCS：保存→全解決で再開依頼（再開後の変換はワーカー側字幕ツールの検出状態に依存するため、ここでは依頼までを検証する） ---
   int resumed=0;foreach(var glyph in collected)resumed=DrcsScreenLogic.SaveReplacement(client,glyph,"髙","");
   var savedMap=Drcs.Load(client.Data);
   if(collected.Any(g=>!savedMap.TryGetValue(g.Hash,out var r)||r.Text!="髙"))throw new InvalidOperationException("DRCS対応表が保存されていません。");
   if(resumed<1)throw new InvalidOperationException("全字形の解決後に再開が依頼されませんでした。");
   Console.WriteLine("DRCS保存OK：対応表 "+savedMap.Count+" 件 / 再開依頼 "+resumed+" 本");
   Console.WriteLine("SMOKE-B2B OK");
   return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
