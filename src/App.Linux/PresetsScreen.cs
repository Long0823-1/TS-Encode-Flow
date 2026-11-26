using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// プリセット編集の入力モデル。Windows版 PresetEditor（src/App/PresetsView.cs）と同じ項目・
// 同じ保存時変換（数値検証の文言、CMカット時の音声コピーOFF/FPSクリア等）を、UI非依存で持つ。
// 画面（PresetEditorView）と検証用スモークの両方がこの経路を通る。
sealed class PresetEditorModel {
 public Preset Working {get;}
 public bool IsNew {get;}
 public string Name,FileName,Fps,ExtraArgs,Speed,AudioStreams,CmTrimFps;
 public string WidthText,HeightText,BitrateText,AudioBitrateText,ThreadsText,ServiceIdText,ChunkSecondsText;
 public string CropLeftText,CropTopText,CropRightText,CropBottomText;
 public int EngineIndex,CodecIndex,ContainerIndex,DeinterlaceIndex,RateModeIndex,SubtitleModeIndex;
 public int CmModeIndex,CmSourceIndex,CadenceIndex,BitDepthIndex,ColorMatrixIndex,InputColorMatrixIndex,ColorRangeIndex,DitherIndex;
 public bool KeepAspect,AllowUpscale,AutoInverseTelecine,Denoise,Sharpen,RemoveLogo,CopyAudio,KeepSubtitles,IgnoreSubtitleErrors,EmbedMetadata;
 public bool CmNoLogo,CmFallbackWithoutLogo,CmReview,PipeGpu,ResumeChunks,AllAudio,DiagnoseSignal,RepairTimeline;
 public double Quality;

 public PresetEditorModel(Preset source,bool isNew) {
  Working=Store.Clone(source);IsNew=isNew;var p=Working;
  // Windows版の名前初期値：新規 or カスタムはそのまま、工場プリセットの編集は「・カスタム」を付けて複製にする
  Name=isNew||string.IsNullOrEmpty(source.FactoryId)?source.Name:source.Name+"・カスタム";
  FileName=p.FileName;Fps=p.Fps;ExtraArgs=p.ExtraArgs;Speed=p.Speed;AudioStreams=p.AudioStreams;CmTrimFps=p.CmTrimFps;
  WidthText=p.Width.ToString();HeightText=p.Height.ToString();BitrateText=p.Bitrate.ToString();AudioBitrateText=p.AudioBitrate.ToString();
  ThreadsText=p.Threads.ToString();ServiceIdText=p.ServiceId.ToString();ChunkSecondsText=p.ChunkSeconds.ToString();
  CropLeftText=p.CropLeft.ToString();CropTopText=p.CropTop.ToString();CropRightText=p.CropRight.ToString();CropBottomText=p.CropBottom.ToString();
  EngineIndex=(int)p.Engine;CodecIndex=(int)p.Codec;ContainerIndex=(int)p.Container;DeinterlaceIndex=(int)p.Deinterlace;
  RateModeIndex=(int)p.RateMode;SubtitleModeIndex=(int)p.SubtitleMode;
  CmModeIndex=(int)p.CmMode;CmSourceIndex=p.CmSource==CmSource.Amatsukaze?1:0;
  CadenceIndex=(int)p.Cadence;BitDepthIndex=p.BitDepth==10?1:0;ColorMatrixIndex=(int)p.ColorMatrix;
  InputColorMatrixIndex=(int)p.InputColorMatrix;ColorRangeIndex=(int)p.ColorRange;DitherIndex=(int)p.Dither;
  KeepAspect=p.KeepAspect;AllowUpscale=p.AllowUpscale;AutoInverseTelecine=p.AutoInverseTelecine;Denoise=p.Denoise;
  Sharpen=p.Sharpen;RemoveLogo=p.RemoveLogo;CopyAudio=p.CopyAudio;KeepSubtitles=p.KeepSubtitles;
  IgnoreSubtitleErrors=p.IgnoreSubtitleErrors;EmbedMetadata=p.EmbedMetadata;
  CmNoLogo=p.CmNoLogo;CmFallbackWithoutLogo=p.CmFallbackWithoutLogo;CmReview=p.CmReview;
  PipeGpu=p.PipeGpu;ResumeChunks=p.ResumeChunks;AllAudio=p.AllAudio;DiagnoseSignal=p.DiagnoseSignal;RepairTimeline=p.RepairTimeline;
  Quality=p.Quality;
 }
 public static string DefaultSpeed(Engine engine)=>engine==Engine.CPU?"medium":engine==Engine.NVEncC?"P4":engine==Engine.Vaapi?"":"balanced";
 static int Num(string text,string label)=>int.TryParse(text,out int value)?value:throw new ArgumentException(label+"を整数で入力してください。");
 public Preset Apply() {
  var p=Working;
  p.Name=string.IsNullOrWhiteSpace(Name)?"カスタム":Name.Trim();p.FileName=FileName?.Trim()??"";
  p.Engine=(Engine)EngineIndex;p.Codec=(Codec)CodecIndex;p.Container=(Container)ContainerIndex;
  p.Width=Num(WidthText,"幅");p.Height=Num(HeightText,"高さ");p.KeepAspect=KeepAspect;p.AllowUpscale=AllowUpscale;
  p.Deinterlace=(Deinterlace)DeinterlaceIndex;p.Quality=(int)Math.Round(Quality);p.RateMode=(RateMode)RateModeIndex;
  p.Bitrate=Num(BitrateText,"映像kbps");p.Fps=Fps??"";p.AutoInverseTelecine=AutoInverseTelecine;
  p.Denoise=Denoise;p.Sharpen=Sharpen;p.RemoveLogo=RemoveLogo;
  p.CopyAudio=CopyAudio;p.AudioBitrate=Num(AudioBitrateText,"音声kbps");
  p.KeepSubtitles=KeepSubtitles;p.IgnoreSubtitleErrors=IgnoreSubtitleErrors;p.SubtitleMode=(SubtitleMode)Math.Max(0,SubtitleModeIndex);
  p.CmMode=(CmMode)CmModeIndex;p.CmSource=CmSourceIndex==1?CmSource.Amatsukaze:CmSource.Sidecar;
  p.CmNoLogo=CmNoLogo;p.CmFallbackWithoutLogo=CmFallbackWithoutLogo;p.CmReview=CmReview;p.CmTrimFps=CmTrimFps??"";
  p.EmbedMetadata=EmbedMetadata;p.Speed=Speed??"";p.Threads=Num(ThreadsText,"スレッド数");p.ServiceId=Num(ServiceIdText,"サービスID");
  p.ExtraArgs=ExtraArgs??"";
  p.CropLeft=Num(CropLeftText,"左クロップ");p.CropTop=Num(CropTopText,"上クロップ");p.CropRight=Num(CropRightText,"右クロップ");p.CropBottom=Num(CropBottomText,"下クロップ");
  p.Cadence=(CadenceMode)CadenceIndex;p.PipeGpu=PipeGpu;p.ResumeChunks=ResumeChunks;p.ChunkSeconds=Num(ChunkSecondsText,"再開区間");
  p.AllAudio=AllAudio;p.AudioStreams=AudioStreams??"";p.BitDepth=BitDepthIndex==1?10:8;
  p.ColorMatrix=(VideoMatrix)ColorMatrixIndex;p.InputColorMatrix=(VideoMatrix)InputColorMatrixIndex;
  p.ColorRange=(VideoRange)ColorRangeIndex;p.Dither=(DitherMode)DitherIndex;
  p.DiagnoseSignal=DiagnoseSignal;p.RepairTimeline=RepairTimeline;
  // Windows版エディタと同じ：CMカットでは音声コピーOFF・FPS指定なしで保存する
  if(p.CmMode==CmMode.Cut){p.CopyAudio=false;p.Fps="";}
  return p;
 }
}

// プリセット画面（Windows版 PresetsView の対応物）。一覧⇔編集を同一画面内で切り替える。
sealed class PresetsScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly ComboBox enginePicker;readonly WrapPanel chips=new(){Orientation=Orientation.Horizontal};
 readonly WrapPanel cards=new(){Orientation=Orientation.Horizontal};
 readonly StackPanel listRoot;readonly Panel host=new();
 string category="すべて";Preset? pendingDelete;
 static readonly string[] Categories=["すべて","通常番組","アニメ","映画","スポーツ","モバイル","カスタム"];

 public PresetsScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;
  enginePicker=Ui.Picker(Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList(),(int)client.Selected.Engine);
  enginePicker.SelectionChanged+=(_,_)=>Build();
  foreach(string name in Categories) {
   string label=name;var b=new Button {Content=label,Margin=new Thickness(0,0,8,8)};
   b.Click+=(_,_)=>{category=label;Build();};
   chips.Children.Add(b);
  }
  var header=Ui.Card(Ui.Stack(
   Ui.Text("プリセット",25,bold:true),
   Ui.Text("12の用途 × 変換方式（Linuxでは VA-API の11種が加わります）。選んでそのまま使うか、自分用に調整できます。",13,muted:true),
   Ui.Field("変換方式",enginePicker),
   Ui.Button("＋ 自作プリセットを追加",AddPreset,bold:true)));
  listRoot=Ui.Stack(header,chips,cards);
  host.Children.Add(Ui.Scroll(listRoot));
  Content=host;
  Build();
 }
 public void Reload() {
  if(host.Children.Count==1&&host.Children[0] is ScrollViewer)Build();
 }
 void AddPreset() {
  var engine=(Engine)Math.Max(0,enginePicker.SelectedIndex);
  var preset=new Preset {Name="自作プリセット",Engine=engine,Speed=PresetEditorModel.DefaultSpeed(engine)};
  OpenEditor(preset,true);
 }
 public void OpenEditor(Preset source,bool isNew) {
  var editor=new PresetEditorView(client,new PresetEditorModel(source,isNew),()=>{
   host.Children.Clear();host.Children.Add(Ui.Scroll(listRoot));
   category="カスタム";enginePicker.SelectedIndex=(int)client.Selected.Engine;Build();
   notice(isNew?"カスタムプリセットを保存して選択しました。":"カスタムプリセットを保存しました。同名がある場合は上書きしました。");
  },()=>{host.Children.Clear();host.Children.Add(Ui.Scroll(listRoot));});
  host.Children.Clear();host.Children.Add(editor);
 }
 void Build() {
  cards.Children.Clear();
  int ei=Math.Max(0,enginePicker.SelectedIndex);
  // カテゴリチップの選択状態
  for(int i=0;i<chips.Children.Count;i++)if(chips.Children[i] is Button b)b.Background=Categories[i]==category?Ui.SelectedBack:null;
  if(pendingDelete!=null) {
   var p=pendingDelete;
   cards.Children.Add(Ui.Card(Ui.Stack(
    Ui.Text("カスタムプリセットを削除",16,bold:true),
    Ui.Text("「"+p.Name+"」を削除しますか？\n登録済みの変換ジョブと固定式の録画後BATは設定を保持します。追従式BATは、このプリセットを削除すると実行できなくなります。",13),
    Ui.Row(Ui.Button("削除する",()=>{pendingDelete=null;try{client.DeletePreset(p);notice("「"+p.Name+"」を削除しました。選択中だった場合は標準プリセットへ切り替えました。");}catch(Exception e){notice(e.Message);}Build();},bold:true),
           Ui.Button("キャンセル",()=>{pendingDelete=null;Build();})))));
  }
  var list=client.Settings.Presets.Where(p=>(int)p.Engine==ei&&(category=="すべて"||p.Category==category)).ToList();
  foreach(var preset in list) {
   var p=preset;bool chosen=ReferenceEquals(client.Selected,p);bool custom=string.IsNullOrEmpty(p.FactoryId);
   var buttons=Ui.Row(
    Ui.Button(chosen?"✓ 選択中":"この設定を使う",()=>{try{client.Choose(p);notice(p.Name+"を選びました。");}catch(Exception e){notice(e.Message);}Build();},!chosen),
    Ui.Button("詳細・カスタマイズ",()=>OpenEditor(p,false)));
   if(custom) {
    buttons.Children.Add(Ui.Button("複製",()=>{var clone=Store.Clone(p);var m=new PresetEditorModel(clone,true);m.Name=p.Name+"・コピー";
     var editor=new PresetEditorView(client,m,()=>{host.Children.Clear();host.Children.Add(Ui.Scroll(listRoot));category="カスタム";enginePicker.SelectedIndex=(int)client.Selected.Engine;Build();notice("カスタムプリセットを複製しました。");},()=>{host.Children.Clear();host.Children.Add(Ui.Scroll(listRoot));});
     host.Children.Clear();host.Children.Add(editor);}));
    buttons.Children.Add(Ui.Button("削除",()=>{pendingDelete=p;Build();}));
   }
   var card=Ui.Card(Ui.Stack(
    Ui.Row(Ui.Pill(p.Category),Ui.Pill(p.Codec+" · "+p.Container)),
    Ui.Text(p.Name,18,bold:true),
    Ui.Text(p.Summary,12,muted:true),
    Ui.Text(PresetCatalog.ResolutionName(p)+"\n"+PresetCatalog.FilterName(p.Deinterlace),12,muted:true),
    buttons),chosen);
   card.Width=452;card.Margin=new Thickness(0,0,12,12);
   cards.Children.Add(card);
  }
  if(list.Count==0)cards.Children.Add(Ui.Text("この条件のプリセットはありません。",15,muted:true));
 }
}

// プリセット編集（Windows版 PresetEditor の対応物）。項目と並びは Windows 版に合わせる。
sealed class PresetEditorView:UserControl {
 public PresetEditorView(EngineClientLinux client,PresetEditorModel m,Action saved,Action cancelled) {
  var error=Ui.Text("",13);error.Foreground=Avalonia.Media.Brushes.Firebrick;
  var name=Ui.Entry(m.Name);var fileName=Ui.Entry(m.FileName,"空欄なら設定画面の共通マクロ");
  var engine=Ui.Picker(Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList(),m.EngineIndex);
  var codec=Ui.Picker(["H.264（互換性重視）","HEVC / H.265（省容量）","AV1（対応機器が必要）"],m.CodecIndex);
  var container=Ui.Picker(["MP4","MKV"],m.ContainerIndex);
  var width=Ui.Entry(m.WidthText);var height=Ui.Entry(m.HeightText);
  var sizeGrid=new Grid {ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=16};
  var wf=Ui.Field("幅",width);var hf=Ui.Field("高さ",height);
  Grid.SetColumn(wf,0);Grid.SetColumn(hf,1);sizeGrid.Children.Add(wf);sizeGrid.Children.Add(hf);
  var aspect=new ToggleSwitch {IsChecked=m.KeepAspect};var upscale=new ToggleSwitch {IsChecked=m.AllowUpscale};
  var filter=Ui.Picker(Enum.GetValues<Deinterlace>().Select(PresetCatalog.FilterName).ToList(),m.DeinterlaceIndex);
  var quality=new Slider {Minimum=1,Maximum=51,Value=m.Quality,MinWidth=320};
  var qualityLabel=Ui.Text("品質 "+m.Quality.ToString("0"),17,bold:true);
  quality.ValueChanged+=(_,e)=>qualityLabel.Text="品質 "+Math.Round(e.NewValue)+"（小さいほど高画質）";
  var rate=Ui.Picker(["品質で指定（おすすめ）","可変ビットレート","固定ビットレート"],m.RateModeIndex);
  var bitrate=Ui.Entry(m.BitrateText);var fps=Ui.Entry(m.Fps,"素材のまま（空欄） / 30000/1001 など");
  var ivtc=new CheckBox {IsChecked=m.AutoInverseTelecine};
  var denoise=new ToggleSwitch {IsChecked=m.Denoise};var sharp=new ToggleSwitch {IsChecked=m.Sharpen};var removeLogo=new ToggleSwitch {IsChecked=m.RemoveLogo};
  var audioCopy=new ToggleSwitch {IsChecked=m.CopyAudio};var audioRate=Ui.Entry(m.AudioBitrateText);
  var subtitles=new ToggleSwitch {IsChecked=m.KeepSubtitles};
  var ignoreSubtitles=new ToggleSwitch {IsChecked=m.IgnoreSubtitleErrors,IsEnabled=m.KeepSubtitles};
  var subtitleMode=Ui.Picker(["変換して格納（従来）","ARIBのまま格納（MKVのみ）","変換＋ARIBの両方（MKVのみ）"],m.SubtitleModeIndex);
  subtitleMode.IsEnabled=m.KeepSubtitles;
  subtitles.IsCheckedChanged+=(_,_)=>{ignoreSubtitles.IsEnabled=subtitles.IsChecked==true;subtitleMode.IsEnabled=subtitles.IsChecked==true;};
  var meta=new ToggleSwitch {IsChecked=m.EmbedMetadata};
  var extra=Ui.Entry(m.ExtraArgs);var speed=Ui.Entry(m.Speed);var threads=Ui.Entry(m.ThreadsText);var sid=Ui.Entry(m.ServiceIdText);
  bool engineInit=true;
  engine.SelectionChanged+=(_,_)=>{if(engineInit)return;var selected=(Engine)Math.Max(0,engine.SelectedIndex);speed.Text=PresetEditorModel.DefaultSpeed(selected);extra.Text="";};
  var cmMode=Ui.Picker(["CM処理しない","CMを残してチャプター・タイムリスト","CMをカットして本編をつなぐ"],m.CmModeIndex);
  var cmSource=Ui.Picker(["EDL / join_logo_scp Trim・手動で指定","Amatsukaze方式でCM候補を自動解析"],m.CmSourceIndex);
  var fallbackLogo=new CheckBox {IsChecked=m.CmFallbackWithoutLogo};var noLogo=new ToggleSwitch {IsChecked=m.CmNoLogo};
  var cmReview=new ToggleSwitch {IsChecked=m.CmReview};var trimFps=Ui.Entry(m.CmTrimFps,"空欄＝入力FPS / 30000/1001など");
  void CmChanged() {
   bool cut=cmMode.SelectedIndex==2;
   audioCopy.IsEnabled=!cut;if(cut)audioCopy.IsChecked=false;
   fps.IsEnabled=!cut;
   fallbackLogo.IsEnabled=cmMode.SelectedIndex!=0&&cmSource.SelectedIndex==1&&noLogo.IsChecked!=true;
  }
  cmSource.SelectionChanged+=(_,_)=>CmChanged();cmMode.SelectionChanged+=(_,_)=>CmChanged();
  noLogo.IsCheckedChanged+=(_,_)=>CmChanged();
  var cmCard=Ui.Card(Ui.Stack(Ui.Text("CM・タイムリスト",21,bold:true),
   Ui.Field("出力方法",cmMode),Ui.Field("区間の取得方法",cmSource),
   Ui.Row(Ui.Text("ロゴを使わない（無音・シーンチェンジのみ）"),noLogo),
   Ui.Row(fallbackLogo,Ui.Text("ロゴ取得失敗時は無音・シーンチェンジのみで続行")),
   Ui.Note("事前にチェックすると、自動ロゴ取得に失敗した場合だけ切り替えます。手動LGDの指定エラー・入力読取りエラー・中止には適用しません。"),
   Ui.Row(Ui.Text("変換前にCM候補を確認する"),cmReview),
   Ui.Note("自動判定には誤検出があります。確認後にCMカット／CMを残す方式を変更できます。CMカットでは音声コピーOFF、FPS指定なしで処理します。"),
   Ui.Field("Trim AVSの基準FPS",trimFps,"外部ツールと同じ入力・フレーム基準が必要です。AVSのコードは実行しません。")));
  var mixed=Ui.Picker(["プリセットのFPS処理","区間別24/30/60fps・VFR（12秒単位）"],m.CadenceIndex);
  var pipe=new CheckBox {IsChecked=m.PipeGpu};var resume=new CheckBox {IsChecked=m.ResumeChunks};var chunk=Ui.Entry(m.ChunkSecondsText);
  var allAudio=new CheckBox {IsChecked=m.AllAudio};var audioIds=Ui.Entry(m.AudioStreams,"空欄＝上の選択 / 例 1,2");
  var depth=Ui.Picker(["8bit","10bit"],m.BitDepthIndex);
  var matrix=Ui.Picker(["維持","BT.709","SMPTE 170M（SD）","BT.2020"],m.ColorMatrixIndex);
  var sourceMatrix=Ui.Picker(["メタデータから取得","BT.709と指定","SMPTE 170Mと指定","BT.2020と指定"],m.InputColorMatrixIndex);
  var range=Ui.Picker(["維持","Limited","Full"],m.ColorRangeIndex);
  var dither=Ui.Picker(["なし","Bayer","誤差拡散"],m.DitherIndex);
  var diagnose=new CheckBox {IsChecked=m.DiagnoseSignal};var repair=new CheckBox {IsChecked=m.RepairTimeline};
  var expertCard=Ui.Card(Ui.Stack(Ui.Text("詳細映像・音声・再開",21,bold:true),
   Ui.Field("FPS方式",mixed),
   Ui.Note("混在VFRは全編を12秒区間で調べ、3:2は23.976p、プログレッシブは維持、判定できないインターレースは59.94p。区間内の短い切り替わりは精密復元しません。"),
   Ui.Row(pipe,Ui.Text("GPUへパイプ送信（可逆中間ファイルを省略）")),
   Ui.Row(resume,Ui.Text("検証済み区間を保存して途中から再開")),
   Ui.Field("再開区間 秒（10～600）",chunk,"混在VFRでは12秒固定。映像・音声の結合は再実行します。"),
   Ui.Row(allAudio,Ui.Text("選択サービス内の音声をすべて保持")),
   Ui.Field("音声ストリーム番号を個別指定",audioIds,"指定時はこちらを優先。番号は「解析・比較」で確認できます。"),
   Ui.Field("ビット深度",depth),Ui.Field("入力の色設定",sourceMatrix),Ui.Field("出力の色域",matrix),Ui.Field("出力レンジ",range),Ui.Field("ディザリング",dither),
   Ui.Note("色域変換はCPUで実施。色域変換時のディザはOFFまたは誤差拡散です。GPUの10bitはHEVC/AV1・対応機器が必要です。詳細パイプラインでは追加引数を空欄にしてください。"),
   Ui.Row(repair,Ui.Text("録画の時刻欠落を詰めて変換（補正モード）")),
   Ui.Note("初期値OFF。欠落した映像は復元できません。対象サービスを再エンコードせずキャッシュへコピーし、補正後の時刻でCM・字幕を処理します。元TS基準のEDL/Trimは使用できません。空き容量が必要です。破損境界の映像乱れ・音ずれ・字幕ずれは残る場合があります。"),
   Ui.Row(diagnose,Ui.Text("変換前にTSドロップ・時刻飛びを全編診断")),
   Ui.Note("診断結果は「解析・比較」に表示。全編読み取りが増えます。")));
  var crops=new[]{Ui.Entry(m.CropLeftText),Ui.Entry(m.CropTopText),Ui.Entry(m.CropRightText),Ui.Entry(m.CropBottomText)};
  foreach(var c in crops)c.MinWidth=70;
  var advanced=Ui.Stack(
   Ui.Field("速度プリセット",speed),Ui.Field("CPUスレッド数（0＝自動）",threads),Ui.Field("サービスID（0＝自動）",sid),Ui.Field("追加引数",extra),
   Ui.Note("クロップ：左・上・右・下（偶数ピクセル）"),Ui.Row(crops));
  advanced.IsVisible=false;
  var advancedToggle=new ToggleSwitch();
  advancedToggle.IsCheckedChanged+=(_,_)=>advanced.IsVisible=advancedToggle.IsChecked==true;
  var save=Ui.Button("プリセットを保存（同名は上書き）",()=> {
   m.Name=name.Text??"";m.FileName=fileName.Text??"";m.EngineIndex=Math.Max(0,engine.SelectedIndex);m.CodecIndex=Math.Max(0,codec.SelectedIndex);
   m.ContainerIndex=Math.Max(0,container.SelectedIndex);m.WidthText=width.Text??"";m.HeightText=height.Text??"";
   m.KeepAspect=aspect.IsChecked==true;m.AllowUpscale=upscale.IsChecked==true;m.DeinterlaceIndex=Math.Max(0,filter.SelectedIndex);
   m.Quality=quality.Value;m.RateModeIndex=Math.Max(0,rate.SelectedIndex);m.BitrateText=bitrate.Text??"";m.Fps=fps.Text??"";
   m.AutoInverseTelecine=ivtc.IsChecked==true;m.Denoise=denoise.IsChecked==true;m.Sharpen=sharp.IsChecked==true;m.RemoveLogo=removeLogo.IsChecked==true;
   m.CopyAudio=audioCopy.IsChecked==true;m.AudioBitrateText=audioRate.Text??"";m.KeepSubtitles=subtitles.IsChecked==true;
   m.IgnoreSubtitleErrors=ignoreSubtitles.IsChecked==true;m.SubtitleModeIndex=Math.Max(0,subtitleMode.SelectedIndex);m.EmbedMetadata=meta.IsChecked==true;
   m.CmModeIndex=Math.Max(0,cmMode.SelectedIndex);m.CmSourceIndex=Math.Max(0,cmSource.SelectedIndex);m.CmNoLogo=noLogo.IsChecked==true;
   m.CmFallbackWithoutLogo=fallbackLogo.IsChecked==true;m.CmReview=cmReview.IsChecked==true;m.CmTrimFps=trimFps.Text??"";
   m.CadenceIndex=Math.Max(0,mixed.SelectedIndex);m.PipeGpu=pipe.IsChecked==true;m.ResumeChunks=resume.IsChecked==true;m.ChunkSecondsText=chunk.Text??"";
   m.AllAudio=allAudio.IsChecked==true;m.AudioStreams=audioIds.Text??"";m.BitDepthIndex=Math.Max(0,depth.SelectedIndex);
   m.ColorMatrixIndex=Math.Max(0,matrix.SelectedIndex);m.InputColorMatrixIndex=Math.Max(0,sourceMatrix.SelectedIndex);
   m.ColorRangeIndex=Math.Max(0,range.SelectedIndex);m.DitherIndex=Math.Max(0,dither.SelectedIndex);
   m.DiagnoseSignal=diagnose.IsChecked==true;m.RepairTimeline=repair.IsChecked==true;
   m.Speed=speed.Text??"";m.ThreadsText=threads.Text??"";m.ServiceIdText=sid.Text??"";m.ExtraArgs=extra.Text??"";
   m.CropLeftText=crops[0].Text??"";m.CropTopText=crops[1].Text??"";m.CropRightText=crops[2].Text??"";m.CropBottomText=crops[3].Text??"";
   try{client.SavePreset(m.Apply());saved();}catch(Exception e){error.Text=e.Message;}
  },bold:true);
  Content=Ui.Scroll(Ui.Stack(
   Ui.Row(Ui.Button("← 一覧に戻る",cancelled),Ui.Text(m.IsNew?"自作プリセットを追加":"プリセットを調整",24,bold:true)),
   Ui.Text(m.Working.Notes,12,muted:true),error,
   Ui.Card(Ui.Stack(
    Ui.Field("名前",name,"同名のカスタムプリセットは上書きします。変換方式を変更した場合も同じ名前に適用します。"),
    Ui.Field("このプリセットのファイル名マクロ",fileName,"空欄は共通設定を使用。例：{if:Episode}{TitleWithoutEpisode}　第{Episode}話{else}{Title}{endif}"),
    Ui.Field("変換方式",engine),Ui.Field("コーデック",codec),Ui.Field("保存形式",container))),
   Ui.Card(Ui.Stack(Ui.Text("解像度",21,bold:true),sizeGrid,Ui.Note("幅・高さがどちらも0なら元の解像度。"),
    Ui.Row(Ui.Text("縦横比を保持"),aspect),Ui.Row(Ui.Text("拡大を許可"),upscale))),
   Ui.Card(Ui.Stack(Ui.Text("画質とフィルタ",21,bold:true),Ui.Field("レート制御",rate),qualityLabel,quality,
    Ui.Field("映像ビットレート kbps",bitrate),
    Ui.Row(ivtc,Ui.Text("逆テレシネを自動判定（3:2 → 23.976fps）")),
    Ui.Note("複数区間が一致した場合のみ適用。不明・混在時は下の解除方式へ戻ります。FPS欄は空欄にしてください。GPUでも判定・復元はCPUで行い、可逆中間ファイルを作成します。"),
    Ui.Field("インターレース解除（判定できない場合）",filter),
    Ui.Field("フレームレート",fps,"空欄なら維持。GPUで指定すると可逆中間ファイルを作成します。"),
    Ui.Row(Ui.Text("ノイズ除去"),denoise),Ui.Row(Ui.Text("シャープ化"),sharp),Ui.Row(Ui.Text("透過ロゴを消す"),removeLogo),
    Ui.Note("登録済み局ロゴ（LGD）を使い、AviSynth＋DelogoHDで透過ロゴを逆算で除去します。事前にツール設定で「ロゴ消し · DelogoHD」のダウンロードと、局ロゴの登録が必要です。中間映像（可逆）を作るため容量と時間が追加で必要です。"))),
   Ui.Card(Ui.Stack(Ui.Text("音声・字幕・番組情報",21,bold:true),
    Ui.Row(Ui.Text("音声をコピー"),audioCopy),Ui.Field("AACビットレート kbps",audioRate),
    Ui.Row(Ui.Text("字幕を動画内に保存"),subtitles),Ui.Row(Ui.Text("字幕エラーを無視して続行"),ignoreSubtitles),
    Ui.Note("ON：未対応DRCSは � に置換。字幕の解析・格納に失敗した場合は字幕なしで続行し、警告を残します。"),
    Ui.Field("字幕の格納形式",subtitleMode),
    Ui.Note("「ARIBのまま」はMKV保存時のみ有効で、ARIB字幕をASS/SRTへ変換せずMatroska規格のS_ARIBSUBとして生のまま格納します。DRCS外字もそのまま残り、DRCS確認も不要になります。「両方」は変換字幕も一緒に入れます。再生にはARIB字幕対応プレイヤー（mpv等）が必要です。"),
    Ui.Row(Ui.Text("番組情報をメタデータに保存"),meta),
    Ui.Note("MP4はテキスト字幕、MKVはASS字幕。DRCSの未対応文字は画像を見て補正できます。"))),
   Ui.Row(Ui.Text("詳細設定を表示"),advancedToggle),advanced,expertCard,cmCard,save));
  engineInit=false;
  CmChanged();
 }
}
