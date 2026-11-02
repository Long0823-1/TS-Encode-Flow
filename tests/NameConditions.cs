using System;
using System.IO;
using TsEncodeFlow;
static partial class Tests {
 static void NameConditionTests() {
  var m=new Metadata {Title="番組",OriginalName="録画",Channel="TOKYO MX1",Episode="12",Width=1920,Height=1080,Start=new DateTime(2026,11,2,21,0,0)};var p=new Preset();
  string format="{if:Episode}{Title}_第{Episode}話{else}{Title}{endif}";
  Check(Names.Render(format,m,p)=="番組_第12話.mp4","if macro takes episode-present branch");m.Episode="";Check(Names.Render(format,m,p)=="番組.mp4","if macro takes episode-missing else branch");
  Check(Names.Render("{if:Width>=1920}HD/{if:Channel==\"TOKYO MX1\"}MX/{else}その他/{endif}{else}SD/{endif}{Title}",m,p)==Path.Combine("HD","MX","番組.mp4"),"nested numeric and quoted Japanese channel conditions produce safe folders");
  Check(Names.Render("{if:!Episode}特番_{endif}{Start:yyyyMMdd}_{Title}",m,p)=="特番_20261102_番組.mp4","negation and date formatting coexist");
  Check(Names.Render("{if:Episode>1}連続{else}単発{endif}_{Title}",m,p)=="単発_番組.mp4","missing numeric episode is false rather than an error");
  p.Engine=Engine.VCEEncC;Check(Names.Render("{IF:Engine==vceencc}GPU_{ELSE}CPU_{ENDIF}{Title}",m,p)=="GPU_番組.mp4","condition fields tags and equality are case insensitive");
  Reject(()=>Names.Render("{if:Episode}{Title}",m,p),"missing endif rejected");Reject(()=>Names.Render("{else}{Title}",m,p),"unmatched else rejected");Reject(()=>Names.Render("{if:Episode}a{else}b{else}c{endif}",m,p),"duplicate else rejected");
  Reject(()=>Names.Render("{if:Unknown}a{endif}{Title}",m,p),"unknown condition fields rejected");Reject(()=>Names.Render("{if:Width>=abc}a{endif}{Title}",m,p),"invalid numeric literal rejected");Reject(()=>Names.Render("{if:Channel>10}a{endif}{Title}",m,p),"numeric comparisons cannot reinterpret channel names");
  Reject(()=>Names.Render("{if:Width>=1920}../{endif}{Title}",m,p),"conditional branches do not bypass path traversal guard");
  var s=new Settings {FileName="{if:Width>=1280}{Title}{else}{OriginalName}{endif}"};s.Validate();s.FileName="{if:Episode}{Title}";Reject(()=>s.Validate(),"settings reject structurally invalid condition before conversion");
 }
}
