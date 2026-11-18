using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TsEncodeFlow;

static partial class Tests {
 // Issue #5: inputs are judged by content (TS sync bytes), not by extension.
 static void M2tsTests(string root) {
  string dir=Path.Combine(root,"M2ts");Directory.CreateDirectory(dir);
  byte[] Packet(int pid,int cc) {
   var p=new byte[188];p[0]=0x47;p[1]=(byte)((pid>>8)&0x1F);p[2]=(byte)(pid&0xFF);p[3]=(byte)(0x10|(cc&0xF));
   for(int i=4;i<188;i++)p[i]=(byte)(i&0xFF);
   return p;
  }
  void WritePackets(string file,bool m2ts,int count) {
   using var stream=File.Create(file);
   for(int i=0;i<count;i++){if(m2ts)stream.Write(new byte[4],0,4);var packet=Packet(100,i);stream.Write(packet,0,packet.Length);}
  }
  string ts188=Path.Combine(dir,"plain.ts");WritePackets(ts188,false,4);
  string renamed=Path.Combine(dir,"renamed.m2ts");WritePackets(renamed,false,4);
  string bdav=Path.Combine(dir,"bdav.m2ts");WritePackets(bdav,true,4);
  string noExt=Path.Combine(dir,"noextension");WritePackets(noExt,false,4);
  Check(TransportStream.PacketSize(ts188)==188,"content detection: 188-byte TS");
  Check(TransportStream.PacketSize(renamed)==188,"content detection: .m2ts holding 188-byte TS");
  Check(TransportStream.PacketSize(bdav)==192,"content detection: .m2ts holding 192-byte packets");
  Check(TransportStream.PacketSize(noExt)==188,"content detection: extensionless TS");
  string fake=Path.Combine(dir,"fake.ts");File.WriteAllText(fake,new string('x',4096));
  Check(TransportStream.PacketSize(fake)==0&&TransportStream.IsDefinitelyNot(fake),"content detection: text file named .ts is not TS");
  string tiny=Path.Combine(dir,"tiny.ts");File.WriteAllText(tiny,"fixture");
  Check(!TransportStream.IsDefinitelyNot(tiny),"content detection: file too short to judge is not rejected at intake");
  string data=Path.Combine(dir,"Queue");using var queue=new QueueManager(data);queue.Paused=true;
  Check(queue.Enqueue(renamed)!=null,"queue accepts .m2ts (188-byte content)");
  Check(queue.Enqueue(bdav)!=null,"queue accepts .m2ts (192-byte content)");
  Reject(()=>queue.Enqueue(fake),"queue rejects non-TS content regardless of extension");
  Reject(()=>QueueManager.Submit(data,new[]{fake}),"inbox submission rejects non-TS content");
  QueueManager.Submit(data,new[]{noExt});queue.Tick();
  Check(queue.Jobs.Any(j=>j.Input==noExt),"inbox submission accepts extensionless TS");
  var report192=SignalDiagnostics.Transport(bdav,0,CancellationToken.None);
  Check(report192.Packets==4&&report192.SyncErrors==0&&report192.ContinuityErrors==0,"signal diagnostics reads 192-byte packets without sync or continuity errors");
  var report188=SignalDiagnostics.Transport(ts188,0,CancellationToken.None);
  Check(report188.Packets==4&&report188.SyncErrors==0&&report188.ContinuityErrors==0,"signal diagnostics still reads 188-byte packets");
  Check(File.Exists(renamed)&&File.Exists(bdav),"content detection preserves source files");
 }
}
