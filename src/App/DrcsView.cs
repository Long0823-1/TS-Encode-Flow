using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class DrcsView:ContentView {
 readonly EngineClient client;readonly Action<string> notice;readonly VerticalStackLayout entries=new(){Spacing=18};
 public DrcsView(EngineClient c,Action<string> message) {
  client=c;notice=message;
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Card(Pixel.Stack(Pixel.Text("読めない文字を、読める文字に。",27,null,true),Pixel.Text("放送のDRCS字形を画像で確認し、参考写真と照らし合わせて代替文字を登録します。\n登録した対応表は、次の録画にも自動で適用されます。",14,Pixel.Muted),Pixel.Row(Pixel.Button("一覧を更新",Build),Pixel.Button("対応表を開く",()=>{Directory.CreateDirectory(Path.Combine(client.Data,"Drcs"));Pickers.Open(Path.Combine(client.Data,"Drcs"));})))),entries));Build();
 }
 void Build() {
  client.Refresh();entries.Children.Clear();var map=Drcs.Load(client.Data);
  var glyphs=client.Jobs.SelectMany(j=>j.Glyphs??[]).GroupBy(g=>g.Hash).Select(g=>g.First()).OrderBy(g=>Drcs.Resolved(g,map)).ToList();
  if(glyphs.Count==0){entries.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text("未確認のDRCSはありません",21,null,true),Pixel.Text("ARIB字幕付きTSを変換すると、検出した字形がここに並びます。\n字幕が存在しない録画やDRCSを含まない字幕では一覧は空になります。",14,Pixel.Muted))));return;}
  foreach(var glyph in glyphs) {
   map.TryGetValue(glyph.Hash,out var previous);string reference=previous?.ReferenceImage??"";
   var replacement=Pixel.Entry(previous?.Text??glyph.Alternative,"例：髙、﨑、☎ …");replacement.FontSize=26;
   var glyphImage=new Image {Source=File.Exists(glyph.Image)?ImageSource.FromFile(glyph.Image):null,HeightRequest=190,Aspect=Aspect.AspectFit};
   var photo=new Image {HeightRequest=190,Aspect=Aspect.AspectFit};if(File.Exists(reference))photo.Source=ImageSource.FromFile(reference);
   var images=new Grid {ColumnDefinitions=new ColumnDefinitionCollection {new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=18};
   images.Add(Pixel.Card(Pixel.Stack(Pixel.Text("放送された字形",13,Pixel.Muted,true),glyphImage),Pixel.Surface,16),0,0);
   images.Add(Pixel.Card(Pixel.Stack(Pixel.Text("参考写真",13,Pixel.Muted,true),photo,Pixel.Button("写真・スクリーンショットを選ぶ",async()=> {
    var files=await Pickers.Images();if(files.Count==0)return;string source=files[0];if(new FileInfo(source).Length>30*1024*1024)throw new ArgumentException("30MB以下の画像を選択してください。");
    string dir=Path.Combine(client.Data,"Drcs","References");Directory.CreateDirectory(dir);reference=Path.Combine(dir,Guid.NewGuid().ToString("N")+Path.GetExtension(source));File.Copy(source,reference);photo.Source=ImageSource.FromFile(reference);
   })),Pixel.Surface,16),1,0);
   var note=Pixel.Text(Drcs.Resolved(glyph,map)?"登録済み／既知の代替文字があります":"未対応 · 代替文字を入力してください",13,Drcs.Resolved(glyph,map)?Color.FromArgb("#167349"):Color.FromArgb("#A45E00"),true);
   var save=Pixel.Button("保存して待機中の変換を再開",()=> {
    Drcs.Save(client.Data,glyph.Hash,replacement.Text??"",reference);var updated=Drcs.Load(client.Data);int resumed=0;
    foreach(var job in client.Jobs.Where(j=>j.State==State.NeedsInput&&j.Glyphs.Any(g=>g.Hash==glyph.Hash)))if(job.Glyphs.All(g=>Drcs.Resolved(g,updated))){client.Send("retry",job.Id);resumed++;}
    note.Text="対応表に保存しました";note.TextColor=Color.FromArgb("#167349");notice(resumed>0?resumed+"本の変換を再開します。":"保存しました。他にも未対応の字形がある場合は続けて入力してください。");
   },true);
   entries.Children.Add(Pixel.Card(Pixel.Stack(note,images,Pixel.Field("この字形に対応する文字",replacement,"自動OCRは行いません。見比べて確認した文字を保存してください。"),Pixel.Text("字幕の前後："+glyph.Context,13,Pixel.Muted),Pixel.Text("字形ID："+glyph.Hash,11,Pixel.Muted),save)));
  }
 }
}
