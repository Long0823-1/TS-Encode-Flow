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
  var page=new PresetEditor(client,preset,()=>{category="カスタム";engine.SelectedIndex=(int)client.Selected.Engine;Build();notice("自作プリセットを追加して選択しました。");},true);
  await Application.Current!.Windows[0].Page!.Navigation.PushAsync(page);
 }
 void Build() {
  cards.Children.Clear();var list=client.Settings.Presets.Where(p=>(int)p.Engine==engine.SelectedIndex&&(category=="すべて"||p.Category==category)).ToList();
  for(int i=0;i<list.Count;i+=2) {
   var grid=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=16};
   for(int n=0;n<2&&i+n<list.Count;n++) {
    var p=list[i+n];bool chosen=ReferenceEquals(client.Selected,p);
    var card=Pixel.Stack(Pixel.Row(Pixel.Pill(p.Category),Pixel.Pill(p.Codec+" · "+p.Container,Color.FromArgb("#EDF2FA"))),Pixel.Text(p.Name,19,null,true),Pixel.Text(p.Summary,13,Pixel.Muted),Pixel.Text(PresetCatalog.ResolutionName(p)+"\n"+PresetCatalog.FilterName(p.Deinterlace),13,Pixel.Muted),Pixel.Button(chosen?"✓ 選択中":"この設定を使う",()=>{client.Choose(p);notice(p.Name+"を選びました。");Build();},!chosen),Pixel.Button("詳細・カスタマイズ",async()=> {
     var page=new PresetEditor(client,p,()=>{Build();notice("カスタムプリセットを保存しました。");});await Application.Current!.Windows[0].Page!.Navigation.PushAsync(page);
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
  var name=Pixel.Entry(isNew?source.Name:source.Name+"・カスタム");var engine=new Picker();engine.ItemsSource=Enum.GetValues<Engine>().Select(PresetCatalog.EngineName).ToList();engine.SelectedIndex=(int)p.Engine;
  var codec=new Picker();codec.ItemsSource=new[]{"H.264（互換性重視）","HEVC / H.265（省容量）","AV1（対応機器が必要）"};codec.SelectedIndex=(int)p.Codec;
  var container=new Picker();container.ItemsSource=new[]{"MP4","MKV"};container.SelectedIndex=(int)p.Container;
  Entry width=Pixel.Entry(p.Width.ToString()),height=Pixel.Entry(p.Height.ToString());width.Keyboard=height.Keyboard=Keyboard.Numeric;
  var aspect=new Switch {IsToggled=p.KeepAspect};var upscale=new Switch {IsToggled=p.AllowUpscale};
  var filter=new Picker();filter.ItemsSource=Enum.GetValues<Deinterlace>().Select(PresetCatalog.FilterName).ToList();filter.SelectedIndex=(int)p.Deinterlace;
  var quality=new Slider {Minimum=1,Maximum=40,Value=p.Quality};var qualityLabel=Pixel.Text("品質 "+p.Quality,18,null,true);quality.ValueChanged+=(s,e)=>qualityLabel.Text="品質 "+Math.Round(e.NewValue)+"（小さいほど高画質）";
  var rate=new Picker();rate.ItemsSource=new[]{"品質で指定（おすすめ）","可変ビットレート","固定ビットレート"};rate.SelectedIndex=(int)p.RateMode;
  var bitrate=Pixel.Entry(p.Bitrate.ToString());var fps=Pixel.Entry(p.Fps,"素材のまま（空欄） / 30000/1001 など");
  var denoise=new Switch {IsToggled=p.Denoise};var sharp=new Switch {IsToggled=p.Sharpen};
  var audioCopy=new Switch {IsToggled=p.CopyAudio};var audioRate=Pixel.Entry(p.AudioBitrate.ToString());
  var subtitles=new Switch {IsToggled=p.KeepSubtitles};var ignoreSubtitles=new Switch {IsToggled=p.IgnoreSubtitleErrors,IsEnabled=p.KeepSubtitles};subtitles.Toggled+=(s,e)=>ignoreSubtitles.IsEnabled=e.Value;var meta=new Switch {IsToggled=p.EmbedMetadata};
  Entry extra=Pixel.Entry(p.ExtraArgs),speed=Pixel.Entry(p.Speed),threads=Pixel.Entry(p.Threads.ToString()),sid=Pixel.Entry(p.ServiceId.ToString());
  engine.SelectedIndexChanged+=(s,e)=>{Engine selected=(Engine)engine.SelectedIndex;speed.Text=selected==Engine.CPU?"medium":selected==Engine.NVEncC?"P4":"balanced";extra.Text="";};
  var cmMode=new Picker {ItemsSource=new[]{"CM処理しない","CMを残してチャプター・タイムリスト","CMをカットして本編をつなぐ"},SelectedIndex=(int)p.CmMode};
  var cmSource=new Picker {ItemsSource=new[]{"EDL / join_logo_scp Trim・手動で指定","Amatsukaze方式でCM候補を自動解析"},SelectedIndex=p.CmSource==CmSource.Amatsukaze?1:0};
  var noLogo=new Switch {IsToggled=p.CmNoLogo};var cmReview=new Switch {IsToggled=p.CmReview};var trimFps=Pixel.Entry(p.CmTrimFps,"空欄＝入力FPS / 30000/1001など");
  void CmChanged(){bool cut=cmMode.SelectedIndex==2;audioCopy.IsEnabled=!cut;if(cut){audioCopy.IsToggled=false;fps.Text="";}fps.IsEnabled=!cut;}cmMode.SelectedIndexChanged+=(s,e)=>CmChanged();CmChanged();
  var cmCard=Pixel.Card(Pixel.Stack(Pixel.Text("CM・タイムリスト",22,null,true),Pixel.Field("出力方法",cmMode),Pixel.Field("区間の取得方法",cmSource),Pixel.Row(Pixel.Text("ロゴを使わない（無音・シーンチェンジのみ）"),noLogo),Pixel.Row(Pixel.Text("変換前にCM候補を確認する"),cmReview),Pixel.Text("自動判定には誤検出があります。確認後にCMカット／CMを残す方式を変更できます。CMカットでは音声コピーOFF、FPS指定なしで処理します。",13,Pixel.Muted),Pixel.Field("Trim AVSの基準FPS",trimFps,"外部ツールと同じ入力・フレーム基準が必要です。AVSのコードは実行しません。")));
  var crops=new[]{p.CropLeft,p.CropTop,p.CropRight,p.CropBottom}.Select(v=>Pixel.Entry(v.ToString())).ToArray();
  int Number(Entry e,string label)=>int.TryParse(e.Text,out int value)?value:throw new ArgumentException(label+"を整数で入力してください。");
  var advanced=Pixel.Stack(Pixel.Field("速度プリセット",speed),Pixel.Field("CPUスレッド数（0＝自動）",threads),Pixel.Field("サービスID（0＝自動）",sid),Pixel.Field("追加引数",extra),Pixel.Text("クロップ：左・上・右・下（偶数ピクセル）",13,Pixel.Muted),Pixel.Row(crops));advanced.IsVisible=false;
  var advancedToggle=new Switch();advancedToggle.Toggled+=(s,e)=>advanced.IsVisible=e.Value;
  var sizeGrid=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=16};sizeGrid.Add(Pixel.Field("幅",width),0,0);sizeGrid.Add(Pixel.Field("高さ",height),1,0);
  var save=Pixel.Button(isNew?"自作プリセットを追加":"カスタムプリセットとして保存",async()=> {
   p.Name=string.IsNullOrWhiteSpace(name.Text)?"カスタム":name.Text.Trim();p.Engine=(Engine)engine.SelectedIndex;p.Codec=(Codec)codec.SelectedIndex;p.Container=(Container)container.SelectedIndex;p.Width=Number(width,"幅");p.Height=Number(height,"高さ");p.KeepAspect=aspect.IsToggled;p.AllowUpscale=upscale.IsToggled;p.Deinterlace=(Deinterlace)filter.SelectedIndex;p.Quality=(int)Math.Round(quality.Value);p.RateMode=(RateMode)rate.SelectedIndex;p.Bitrate=Number(bitrate,"映像kbps");p.Fps=fps.Text??"";p.Denoise=denoise.IsToggled;p.Sharpen=sharp.IsToggled;p.CopyAudio=audioCopy.IsToggled;p.AudioBitrate=Number(audioRate,"音声kbps");p.KeepSubtitles=subtitles.IsToggled;p.IgnoreSubtitleErrors=ignoreSubtitles.IsToggled;p.CmMode=(CmMode)cmMode.SelectedIndex;p.CmSource=cmSource.SelectedIndex==1?CmSource.Amatsukaze:CmSource.Sidecar;p.CmNoLogo=noLogo.IsToggled;p.CmReview=cmReview.IsToggled;p.CmTrimFps=trimFps.Text??"";p.EmbedMetadata=meta.IsToggled;p.Speed=speed.Text??"";p.Threads=Number(threads,"スレッド数");p.ServiceId=Number(sid,"サービスID");p.ExtraArgs=extra.Text??"";p.CropLeft=Number(crops[0],"左クロップ");p.CropTop=Number(crops[1],"上クロップ");p.CropRight=Number(crops[2],"右クロップ");p.CropBottom=Number(crops[3],"下クロップ");client.SavePreset(p);saved();await Navigation.PopAsync();
  },true);
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Text(isNew?"自作プリセットを追加":"自分の見やすさに、合わせる。",28,null,true),Pixel.Text(p.Notes,13,Pixel.Muted),Pixel.Card(Pixel.Stack(Pixel.Field("名前",name),Pixel.Field("変換方式",engine),Pixel.Field("コーデック",codec),Pixel.Field("保存形式",container))),Pixel.Card(Pixel.Stack(Pixel.Text("解像度",22,null,true),sizeGrid,Pixel.Text("幅・高さがどちらも0なら元の解像度。",13,Pixel.Muted),Pixel.Row(Pixel.Text("縦横比を保持"),aspect),Pixel.Row(Pixel.Text("拡大を許可"),upscale))),Pixel.Card(Pixel.Stack(Pixel.Text("画質とフィルタ",22,null,true),rate,qualityLabel,quality,Pixel.Field("映像ビットレート kbps",bitrate),Pixel.Field("インターレース解除",filter),Pixel.Field("フレームレート",fps,"空欄なら維持。GPUで指定すると可逆中間ファイルを作成します。"),Pixel.Row(Pixel.Text("ノイズ除去"),denoise),Pixel.Row(Pixel.Text("シャープ化"),sharp))),Pixel.Card(Pixel.Stack(Pixel.Text("音声・字幕・番組情報",22,null,true),Pixel.Row(Pixel.Text("音声をコピー"),audioCopy),Pixel.Field("AACビットレート kbps",audioRate),Pixel.Row(Pixel.Text("字幕を動画内に保存"),subtitles),Pixel.Row(Pixel.Text("字幕エラーを無視して続行"),ignoreSubtitles),Pixel.Text("ON：未対応DRCSは � に置換。字幕の解析・格納に失敗した場合は字幕なしで続行し、警告を残します。",13,Pixel.Muted),Pixel.Row(Pixel.Text("番組情報をメタデータに保存"),meta),Pixel.Text("MP4はテキスト字幕、MKVはASS字幕。DRCSの未対応文字は画像を見て補正できます。",13,Pixel.Muted))),Pixel.Row(Pixel.Text("詳細設定を表示"),advancedToggle),advanced,cmCard,save));
 }
}
