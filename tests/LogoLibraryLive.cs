using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 static async Task LogoLibraryLive(string data,string input,string sourceLogo,string toolData) {
  Directory.CreateDirectory(data);var config=Integration.LoadSettings(toolData);var preset=new Preset {CmMode=CmMode.Chapters,CmSource=CmSource.Amatsukaze};var metadata=await MetadataReader.Read(input,config,0,CancellationToken.None,Console.WriteLine);
  var job=new Job {Id="library-smoke",Input=input,Fingerprint=Store.Fingerprint(input),Metadata=metadata,Preset=preset};
  byte[] original=File.ReadAllBytes(sourceLogo);if(!Encoding.ASCII.GetString(original,0,28).StartsWith("<logo data file ver0.1>"))throw new Exception("test requires single v0.1 logo");string v2=Path.Combine(data,"manual-v2.lgd2");
  using(var w=new BinaryWriter(File.Create(v2))){byte[] signature=new byte[28];Encoding.ASCII.GetBytes("<logo data file ver0.2>").CopyTo(signature,0);w.Write(signature);w.Write(new byte[]{0,0,0,1});w.Write(new byte[256]);w.Write(original,64,16);w.Write(new byte[240]);w.Write(original,80,original.Length-80);}
  var station=new StationLogo {Key=StationLogos.Key(job),Channel="手動追加LGD2の検証",Width=metadata.Width,Height=metadata.Height};string registered=LogoLibrary.Import(data,station,v2,Store.Fingerprint(v2));Console.WriteLine("MANUAL LGD2 IMPORTED "+registered);
  var plan=await CmAnalysis.Analyze(job,config,data,CancellationToken.None,Console.WriteLine,()=>Console.WriteLine(job.Message));Store.Save(Path.Combine(data,"plan.json"),plan);
  if(plan.Source!="logoframe / chapter_exe / join_logo_scp")throw new Exception("manual logo was not used");Console.WriteLine("MANUAL LGD2 NATIVE CM PLAN PASS");
 }
}
