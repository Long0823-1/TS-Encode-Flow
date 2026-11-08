using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class PresetsView:ContentView {
 readonly EngineClient client;readonly Action<string> notice;readonly VerticalStackLayout cards=new(){Spacing=16};
 readonly Picker engine=new(){Title="変換方式"};string category="すべて";
 public PresetsView(EngineClient c,Action<string> message) {
  client=c;notice=message;engine.ItemsSource=Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList();engine.SelectedIndex=(int)c.Selected.Engine;engine.SelectedIndexChanged+=(s,e)=>Build();
  var tags=new FlexLayout {Wrap=Microsoft.Maui.Layouts.FlexWrap.Wrap};foreach(string name in new[]{"すべて","通常番組","アニメ","映画","スポーツ","モバイル","カスタム"}){string label=name;var button=Pixel.Button(name,()=>{category=label;Build();});button.Margin=new Thickness(0,0,8,8);tags.Children.Add(button);}
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Card(Pixel.Stack(Pixel.Text("48種類から、ぴったりを。",25,null,true),Pixel.Text("12の用途 × 4つの変換方式。選んでそのまま使うか、自分用に調整できます。",14,Pixel.Muted),engine,Pixel.Button("＋ 自作プリセットを追加",AddPreset,true))),tags,cards));Build();
 }
 async Task AddPreset() {
  var selectedEngine=(Engine)Math.Max(0,engine.SelectedIndex);
  var preset=new Preset {Name="自作プリセット",Engine=selectedEngine,Speed=selectedEngine==Engine.CPU?"medium":selectedEngine==Engine.NVEncC?"P4":"balanced"};
  var page=new PresetEditor(client,preset,()=>{category="カスタム";engine.SelectedIndex=(int)client.Selected.Engine;Build();notice("カスタムプリセットを保存して選択しました。");},true);
  await Application.Current!.Windows[0].Page!.Navigation.PushAsync(page);
 }
 void Build() {
  cards.Children.Clear();var list=client.Settings.Presets.Where(p=>(int)p.Engine==engine.SelectedIndex&&(category=="すべて"||p.Category==category)).ToList();
  for(int i=0;i<list.Count;i+=2) {
   var grid=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=16};
   for(int n=0;n<2&&i+n<list.Count;n++) {
    var p=list[i+n];bool chosen=ReferenceEquals(client.Selected,p);
    var card=Pixel.Stack(Pixel.Row(Pixel.Pill(p.Category),Pixel.Pill(p.Codec+" · "+p.Container,Color.FromArgb("#EDF2FA"))),Pixel.Text(p.Name,19,null,true),Pixel.Text(p.Summary,13,Pixel.Muted),Pixel.Text(PresetCatalog.ResolutionName(p)+"\n"+PresetCatalog.FilterName(p.Deinterlace),13,Pixel.Muted),Pixel.Button(chosen?"✓ 選択中":"この設定を使う",()=>{client.Choose(p);notice(p.Name+"を選びました。");Build();},!chosen),Pixel.Button("詳細・カスタマイズ",async()=> {
     var page=new PresetEditor(client,p,()=>{category="カスタム";engine.SelectedIndex=(int)client.Selected.Engine;Build();notice("カスタムプリセットを保存しました。同名がある場合は上書きしました。");});await Application.Current!.Windows[0].Page!.Navigation.PushAsync(page);
    }));
    if(string.IsNullOrEmpty(p.FactoryId))card.Children.Add(Pixel.Button("このカスタムプリセットを削除",async()=> {
     var page=Application.Current!.Windows[0].Page!;
     if(!await page.DisplayAlert("カスタムプリセットを削除", "「"+p.Name+"」を削除しますか？\n登録済みの変換ジョブと作成済みの録画後BATは、その設定を保持します。", "削除する", "キャンセル"))return;
     client.DeletePreset(p);Build();notice("「"+p.Name+"」を削除しました。選択中だった場合は標準プリセットへ切り替えました。");
    }));
    grid.Add(Pixel.Card(card,chosen?Color.FromArgb("#E8F0FE"):Colors.White),n,0);
   }
   cards.Children.Add(grid);
  }
  if(list.Count==0)cards.Children.Add(Pixel.Text("この条件のプリセットはありません。",16,Pixel.Muted));
 }
}
sealed class PresetEditor:ContentPage {
 public PresetEditor(EngineClient client,Preset source,Action saved,bool isNew=false) {
  Title=isNew?"自作プリセットを追加":"プリセットを調整";Padding=new Thickness(30);var p=Store.Clone(source);
  var name=Pixel.Entry(isNew||string.IsNullOrEmpty(source.FactoryId)?source.Name:source.Name+"・カスタム");var engine=new Picker();engine.ItemsSource=Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList();engine.SelectedIndex=(int)p.Engine;
  var codec=new Picker();codec.ItemsSource=new[]{"H.264（互換性重視）","HEVC / H.265（省容量）","AV1（対応機器が必要）"};codec.SelectedIndex=(int)p.Codec;
  var container=new Picker();container.ItemsSource=new[]{"MP4","MKV"};container.SelectedIndex=(int)p.Container;
  Entry width=Pixel.Entry(p.Width.ToString()),height=Pixel.Entry(p.Height.ToString());width.Keyboard=height.Keyboard=Keyboard.Numeric;
  var aspect=new Switch {IsToggled=p.KeepAspect};var upscale=new Switch {IsToggled=p.AllowUpscale};
  var filter=new Picker();filter.ItemsSource=Enum.GetValues<Deinterlace>().Select(PresetCatalog.FilterName).ToList();filter.SelectedIndex=(int)p.Deinterlace;
  var quality=new Slider {Minimum=1,Maximum=51,Value=p.Quality};var qualityLabel=Pixel.Text("品質 "+p.Quality,18,null,true);quality.ValueChanged+=(s,e)=>qualityLabel.Text="品質 "+Math.Round(e.NewValue)+"（小さいほど高画質）";
  var rate=new Picker();rate.ItemsSource=new[]{"品質で指定（おすすめ）","可変ビットレート","固定ビットレート"};rate.SelectedIndex=(int)p.RateMode;
  var bitrate=Pixel.Entry(p.Bitrate.ToString());var fps=Pixel.Entry(p.Fps,"素材のまま（空欄） / 30000/1001 など");
  var ivtc=new CheckBox {IsChecked=p.AutoInverseTelecine};
  var denoise=new Switch {IsToggled=p.Denoise};var sharp=new Switch {IsToggled=p.Sharpen};
  var audioCopy=new Switch {IsToggled=p.CopyAudio};var audioRate=Pixel.Entry(p.AudioBitrate.ToString());
  var subtitles=new Switch {IsToggled=p.KeepSubtitles};var ignoreSubtitles=new Switch {IsToggled=p.IgnoreSubtitleErrors,IsEnabled=p.KeepSubtitles};var subtitleMode=new Picker {ItemsSource=new[]{"変換して格納（従来）","ARIBのまま格納（MKVのみ）","変換＋ARIBの両方（MKVのみ）"},SelectedIndex=(int)p.SubtitleMode,IsEnabled=p.KeepSubtitles};subtitles.Toggled+=(s,e)=>{ignoreSubtitles.IsEnabled=e.Value;subtitleMode.IsEnabled=e.Value;};var meta=new Switch {IsToggled=p.EmbedMetadata};
  Entry extra=Pixel.Entry(p.ExtraArgs),speed=Pixel.Entry(p.Speed),threads=Pixel.Entry(p.Threads.ToString()),sid=Pixel.Entry(p.ServiceId.ToString());
  engine.SelectedIndexChanged+=(s,e)=>{Engine selected=(Engine)engine.SelectedIndex;speed.Text=selected==Engine.CPU?"medium":selected==Engine.NVEncC?"P4":"balanced";extra.Text="";};
  var cmMode=new Picker {ItemsSource=new[]{"CM処理しない","CMを残してチャプター・タイムリスト","CMをカットして本編をつなぐ"},SelectedIndex=(int)p.CmMode};
  var cmSource=new Picker {ItemsSource=new[]{"EDL / join_logo_scp Trim・手動で指定","Amatsukaze方式でCM候補を自動解析"},SelectedIndex=p.CmSource==CmSource.Amatsukaze?1:0};
  var fallbackLogo=new CheckBox {IsChecked=p.CmFallbackWithoutLogo};var noLogo=new Switch {IsToggled=p.CmNoLogo};var cmReview=new Switch {IsToggled=p.CmReview};var trimFps=Pixel.Entry(p.CmTrimFps,"空欄＝入力FPS / 30000/1001など");
  void CmChanged(){bool cut=cmMode.SelectedIndex==2;audioCopy.IsEnabled=!cut;if(cut){audioCopy.IsToggled=false;fps.Text="";}fps.IsEnabled=!cut;fallbackLogo.IsEnabled=cmMode.SelectedIndex!=0&&cmSource.SelectedIndex==1&&!noLogo.IsToggled;}cmSource.SelectedIndexChanged+=(s,e)=>CmChanged();noLogo.Toggled+=(s,e)=>CmChanged();cmMode.SelectedIndexChanged+=(s,e)=>CmChanged();CmChanged();
  var cmCard=Pixel.Card(Pixel.Stack(Pixel.Text("CM・タイムリスト",22,null,true),Pixel.Field("出力方法",cmMode),Pixel.Field("区間の取得方法",cmSource),Pixel.Row(Pixel.Text("ロゴを使わない（無音・シーンチェンジのみ）"),noLogo),Pixel.Row(fallbackLogo,Pixel.Text("ロゴ取得失敗時は無音・シーンチェンジのみで続行")),Pixel.Text("事前にチェックすると、自動ロゴ取得に失敗した場合だけ切り替えます。手動LGDの指定エラー・入力読取りエラー・中止には適用しません。",13,Pixel.Muted),Pixel.Row(Pixel.Text("変換前にCM候補を確認する"),cmReview),Pixel.Text("自動判定には誤検出があります。確認後にCMカット／CMを残す方式を変更できます。CMカットでは音声コピーOFF、FPS指定なしで処理します。",13,Pixel.Muted),Pixel.Field("Trim AVSの基準FPS",trimFps,"外部ツールと同じ入力・フレーム基準が必要です。AVSのコードは実行しません。")));
  var mixed=new Picker {ItemsSource=new[]{"プリセットのFPS処理","区間別24/30/60fps・VFR（12秒単位）"},SelectedIndex=(int)p.Cadence};
  var pipe=new CheckBox {IsChecked=p.PipeGpu};var resume=new CheckBox {IsChecked=p.ResumeChunks};var chunk=Pixel.Entry(p.ChunkSeconds.ToString());
  var allAudio=new CheckBox {IsChecked=p.AllAudio};var audioIds=Pixel.Entry(p.AudioStreams,"空欄＝上の選択 / 例 1,2");
  var depth=new Picker {ItemsSource=new[]{"8bit","10bit"},SelectedIndex=p.BitDepth==10?1:0};
  var matrix=new Picker {ItemsSource=new[]{"維持","BT.709","SMPTE 170M（SD）","BT.2020"},SelectedIndex=(int)p.ColorMatrix};
  var sourceMatrix=new Picker {ItemsSource=new[]{"メタデータから取得","BT.709と指定","SMPTE 170Mと指定","BT.2020と指定"},SelectedIndex=(int)p.InputColorMatrix};
  var range=new Picker {ItemsSource=new[]{"維持","Limited","Full"},SelectedIndex=(int)p.ColorRange};
  var dither=new Picker {ItemsSource=new[]{"なし","Bayer","誤差拡散"},SelectedIndex=(int)p.Dither};var diagnose=new CheckBox {IsChecked=p.DiagnoseSignal};
  var repair=new CheckBox {IsChecked=p.RepairTimeline};
  var expertCard=Pixel.Card(Pixel.Stack(Pixel.Text("詳細映像・音声・再開",22,null,true),Pixel.Field("FPS方式",mixed),Pixel.Text("混在VFRは全編を12秒区間で調べ、3:2は23.976p、プログレッシブは維持、判定できないインターレースは59.94p。区間内の短い切り替わりは精密復元しません。",13,Pixel.Muted),Pixel.Row(pipe,Pixel.Text("GPUへパイプ送信（可逆中間ファイルを省略）")),Pixel.Row(resume,Pixel.Text("検証済み区間を保存して途中から再開")),Pixel.Field("再開区間 秒（10～600）",chunk,"混在VFRでは12秒固定。映像・音声の結合は再実行します。"),Pixel.Row(allAudio,Pixel.Text("選択サービス内の音声をすべて保持")),Pixel.Field("音声ストリーム番号を個別指定",audioIds,"指定時はこちらを優先。番号は「解析・比較」で確認できます。"),Pixel.Field("ビット深度",depth),Pixel.Field("入力の色設定",sourceMatrix),Pixel.Field("出力の色域",matrix),Pixel.Field("出力レンジ",range),Pixel.Field("ディザリング",dither),Pixel.Text("色域変換はCPUで実施。色域変換時のディザはOFFまたは誤差拡散です。GPUの10bitはHEVC/AV1・対応機器が必要です。詳細パイプラインでは追加引数を空欄にしてください。",13,Pixel.Muted),Pixel.Row(repair,Pixel.Text("録画の時刻欠落を詰めて変換（補正モード）")),Pixel.Text("初期値OFF。欠落した映像は復元できません。対象サービスを再エンコードせずキャッシュへコピーし、補正後の時刻でCM・字幕を処理します。元TS基準のEDL/Trimは使用できません。空き容量が必要です。破損境界の映像乱れ・音ずれ・字幕ずれは残る場合があります。",13,Pixel.Muted),Pixel.Row(diagnose,Pixel.Text("変換前にTSドロップ・時刻飛びを全編診断")),Pixel.Text("診断結果は「解析・比較」に表示。全編読み取りが増えます。",13,Pixel.Muted)));
  var crops=new[]{p.CropLeft,p.CropTop,p.CropRight,p.CropBottom}.Select(v=>Pixel.Entry(v.ToString())).ToArray();
  int Number(Entry e,string label)=>int.TryParse(e.Text,out int value)?value:throw new ArgumentException(label+"を整数で入力してください。");
  var advanced=Pixel.Stack(Pixel.Field("速度プリセット",speed),Pixel.Field("CPUスレッド数（0＝自動）",threads),Pixel.Field("サービスID（0＝自動）",sid),Pixel.Field("追加引数",extra),Pixel.Text("クロップ：左・上・右・下（偶数ピクセル）",13,Pixel.Muted),Pixel.Row(crops));advanced.IsVisible=false;
  var advancedToggle=new Switch();advancedToggle.Toggled+=(s,e)=>advanced.IsVisible=e.Value;
  var sizeGrid=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=16};sizeGrid.Add(Pixel.Field("幅",width),0,0);sizeGrid.Add(Pixel.Field("高さ",height),1,0);
  var save=Pixel.Button("プリセットを保存（同名は上書き）",async()=> {
   p.Cadence=(CadenceMode)mixed.SelectedIndex;p.PipeGpu=pipe.IsChecked;p.ResumeChunks=resume.IsChecked;p.ChunkSeconds=Number(chunk,"再開区間");p.AllAudio=allAudio.IsChecked;p.AudioStreams=audioIds.Text??"";p.BitDepth=depth.SelectedIndex==1?10:8;p.ColorMatrix=(VideoMatrix)matrix.SelectedIndex;p.InputColorMatrix=(VideoMatrix)sourceMatrix.SelectedIndex;p.ColorRange=(VideoRange)range.SelectedIndex;p.Dither=(DitherMode)dither.SelectedIndex;p.DiagnoseSignal=diagnose.IsChecked;p.RepairTimeline=repair.IsChecked;
   p.Name=string.IsNullOrWhiteSpace(name.Text)?"カスタム":name.Text.Trim();p.Engine=(Engine)engine.SelectedIndex;p.Codec=(Codec)codec.SelectedIndex;p.Container=(Container)container.SelectedIndex;p.Width=Number(width,"幅");p.Height=Number(height,"高さ");p.KeepAspect=aspect.IsToggled;p.AllowUpscale=upscale.IsToggled;p.Deinterlace=(Deinterlace)filter.SelectedIndex;p.Quality=(int)Math.Round(quality.Value);p.RateMode=(RateMode)rate.SelectedIndex;p.Bitrate=Number(bitrate,"映像kbps");p.Fps=fps.Text??"";p.AutoInverseTelecine=ivtc.IsChecked;p.Denoise=denoise.IsToggled;p.Sharpen=sharp.IsToggled;p.CopyAudio=audioCopy.IsToggled;p.AudioBitrate=Number(audioRate,"音声kbps");p.KeepSubtitles=subtitles.IsToggled;p.IgnoreSubtitleErrors=ignoreSubtitles.IsToggled;p.SubtitleMode=(SubtitleMode)Math.Max(0,subtitleMode.SelectedIndex);p.CmMode=(CmMode)cmMode.SelectedIndex;p.CmSource=cmSource.SelectedIndex==1?CmSource.Amatsukaze:CmSource.Sidecar;p.CmNoLogo=noLogo.IsToggled;p.CmFallbackWithoutLogo=fallbackLogo.IsChecked;p.CmReview=cmReview.IsToggled;p.CmTrimFps=trimFps.Text??"";p.EmbedMetadata=meta.IsToggled;p.Speed=speed.Text??"";p.Threads=Number(threads,"スレッド数");p.ServiceId=Number(sid,"サービスID");p.ExtraArgs=extra.Text??"";p.CropLeft=Number(crops[0],"左クロップ");p.CropTop=Number(crops[1],"上クロップ");p.CropRight=Number(crops[2],"右クロップ");p.CropBottom=Number(crops[3],"下クロップ");client.SavePreset(p);saved();await Navigation.PopAsync();
  },true);
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Text(isNew?"自作プリセットを追加":"自分の見やすさに、合わせる。",28,null,true),Pixel.Text(p.Notes,13,Pixel.Muted),Pixel.Card(Pixel.Stack(Pixel.Field("名前",name,"同名のカスタムプリセットは上書きします。変換方式を変更した場合も同じ名前に適用します。"),Pixel.Field("変換方式",engine),Pixel.Field("コーデック",codec),Pixel.Field("保存形式",container))),Pixel.Card(Pixel.Stack(Pixel.Text("解像度",22,null,true),sizeGrid,Pixel.Text("幅・高さがどちらも0なら元の解像度。",13,Pixel.Muted),Pixel.Row(Pixel.Text("縦横比を保持"),aspect),Pixel.Row(Pixel.Text("拡大を許可"),upscale))),Pixel.Card(Pixel.Stack(Pixel.Text("画質とフィルタ",22,null,true),rate,qualityLabel,quality,Pixel.Field("映像ビットレート kbps",bitrate),Pixel.Row(ivtc,Pixel.Text("逆テレシネを自動判定（3:2 → 23.976fps）")),Pixel.Text("複数区間が一致した場合のみ適用。不明・混在時は下の解除方式へ戻ります。FPS欄は空欄にしてください。GPUでも判定・復元はCPUで行い、可逆中間ファイルを作成します。",13,Pixel.Muted),Pixel.Field("インターレース解除（判定できない場合）",filter),Pixel.Field("フレームレート",fps,"空欄なら維持。GPUで指定すると可逆中間ファイルを作成します。"),Pixel.Row(Pixel.Text("ノイズ除去"),denoise),Pixel.Row(Pixel.Text("シャープ化"),sharp))),Pixel.Card(Pixel.Stack(Pixel.Text("音声・字幕・番組情報",22,null,true),Pixel.Row(Pixel.Text("音声をコピー"),audioCopy),Pixel.Field("AACビットレート kbps",audioRate),Pixel.Row(Pixel.Text("字幕を動画内に保存"),subtitles),Pixel.Row(Pixel.Text("字幕エラーを無視して続行"),ignoreSubtitles),Pixel.Text("ON：未対応DRCSは � に置換。字幕の解析・格納に失敗した場合は字幕なしで続行し、警告を残します。",13,Pixel.Muted),Pixel.Field("字幕の格納形式",subtitleMode),Pixel.Text("「ARIBのまま」はMKV保存時のみ有効で、ARIB字幕をASS/SRTへ変換せずMatroska規格のS_ARIBSUBとして生のまま格納します。DRCS外字もそのまま残り、DRCS確認も不要になります。「両方」は変換字幕も一緒に入れます。再生にはARIB字幕対応プレイヤー（mpv等）が必要です。",13,Pixel.Muted),Pixel.Row(Pixel.Text("番組情報をメタデータに保存"),meta),Pixel.Text("MP4はテキスト字幕、MKVはASS字幕。DRCSの未対応文字は画像を見て補正できます。",13,Pixel.Muted))),Pixel.Row(Pixel.Text("詳細設定を表示"),advancedToggle),advanced,expertCard,cmCard,save));
 }
}
