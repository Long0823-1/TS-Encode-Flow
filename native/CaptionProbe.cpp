#include <windows.h>
#include <aribcaption/decoder.hpp>
#include <fstream>
#include <filesystem>
#include <vector>
#include <map>
#include <algorithm>
#include "sample_data.h"
using namespace aribcaption;
namespace fs=std::filesystem;
static std::string quote(const std::string& value){std::string r="\"";for(unsigned char c:value){if(c=='"'||c=='\\'){r+='\\';r+=c;}else if(c<32){char b[8];sprintf_s(b,"\\u%04x",c);r+=b;}else r+=c;}return r+'"';}
static void bmp(const fs::path& file,const DRCS& d) {
 if(d.width<=0||d.height<=0||d.width>256||d.height>256||d.depth_bits<1||d.depth_bits>8)return;
 int scale=std::max(1,192/std::max(d.width,d.height)),w=d.width*scale,h=d.height*scale,stride=(w*3+3)&~3;
 BITMAPFILEHEADER f{};BITMAPINFOHEADER b{};f.bfType=0x4d42;f.bfOffBits=sizeof(f)+sizeof(b);f.bfSize=f.bfOffBits+stride*h;b.biSize=sizeof(b);b.biWidth=w;b.biHeight=h;b.biPlanes=1;b.biBitCount=24;
 std::ofstream out(file,std::ios::binary);out.write((char*)&f,sizeof(f));out.write((char*)&b,sizeof(b));std::vector<unsigned char> row(stride,255);
 for(int y=h-1;y>=0;y--){std::fill(row.begin(),row.end(),255);for(int x=0;x<w;x++){int bit=((y/scale)*d.width+x/scale)*d.depth_bits,value=0;for(int n=0;n<d.depth_bits;n++){int pos=bit+n;if((size_t)(pos/8)<d.pixels.size())value=(value<<1)|((d.pixels[pos/8]>>(7-pos%8))&1);}unsigned char c=(unsigned char)(255-value*255/std::max(1,d.depth-1));row[x*3]=row[x*3+1]=row[x*3+2]=c;}out.write((char*)row.data(),row.size());}
}
struct Cue {int64_t pts,duration;std::string text;};
struct Glyph {std::string hash,image,alternative,context;};
class Exporter {
 Context context;Decoder decoder;fs::path folder;std::vector<Cue> cues;std::map<std::string,Glyph> glyphs;int decodeErrors=0;
public:
 Exporter(fs::path out):decoder(context),folder(out){fs::create_directories(folder);decoder.Initialize();}
 void Decode(const unsigned char* data,size_t size,int64_t pts) {
  DecodeResult result;auto status=decoder.Decode(data,size,pts,result);if(status==DecodeStatus::kError){decodeErrors++;return;}if(status!=DecodeStatus::kGotCaption)return;const Caption& c=*result.caption;std::string text;
  for(const auto& region:c.regions){if(region.is_ruby)continue;if(!text.empty())text+='\n';for(const auto& ch:region.chars){
   if(ch.type==CaptionCharType::kDRCS||ch.type==CaptionCharType::kDRCSReplaced){auto it=c.drcs_map.find(ch.drcs_code);if(it==c.drcs_map.end()){text+="\xef\xbf\xbd";continue;}const DRCS& d=it->second;std::string key=d.md5+"_"+std::to_string(d.width)+"_"+std::to_string(d.height)+"_"+std::to_string(d.depth_bits);
    if(!glyphs.count(key)){std::string image=key+".bmp";bmp(folder/image,d);glyphs[key]={key,image,d.alternative_text,c.text};}text+="{{DRCS:"+key+"}}";
   }else text+=ch.u8str;
  }}
  cues.push_back({c.pts,c.wait_duration==DURATION_INDEFINITE?-1:c.wait_duration,text});
 }
 void Save(unsigned long long transportPackets=0,unsigned long long subtitlePackets=0) {
  std::ofstream out(folder/"captions.json",std::ios::binary);out<<"{\"SchemaVersion\":2,\"TransportPackets\":"<<transportPackets<<",\"SubtitlePackets\":"<<subtitlePackets<<",\"DecodeErrors\":"<<decodeErrors<<",\"Cues\":[";bool first=true;for(const auto& c:cues){if(!first)out<<",";first=false;out<<"{\"Pts\":"<<c.pts<<",\"Duration\":"<<c.duration<<",\"Text\":"<<quote(c.text)<<"}";}
  out<<"],\"Glyphs\":[";first=true;for(const auto& entry:glyphs){const auto& g=entry.second;if(!first)out<<",";first=false;out<<"{\"Hash\":"<<quote(g.hash)<<",\"Image\":"<<quote(g.image)<<",\"Alternative\":"<<quote(g.alternative)<<",\"Context\":"<<quote(g.context)<<"}";}out<<"]}";
 }
};
static int64_t pts(const unsigned char* p){return (((int64_t)(p[0]&14))<<29)|((int64_t)p[1]<<22)|((int64_t)(p[2]&254)<<14)|((int64_t)p[3]<<7)|((p[4]&254)>>1);}
int wmain(int argc,wchar_t** argv) {
 try {
  if(argc==3&&wcscmp(argv[1],L"--self-test")==0){Exporter e(argv[2]);e.Decode(sample_data_drcs_1,sizeof(sample_data_drcs_1),1000);e.Save();return 0;}
  if(argc!=4)return 2;int pid=_wtoi(argv[2]);if(pid<0||pid>8191)return 3;Exporter out(argv[3]);std::ifstream input(fs::path(argv[1]),std::ios::binary);if(!input)return 4;
  std::vector<unsigned char> pes;unsigned char p[188];int last=-1;bool valid=false;int64_t lastPts=-1,wrap=0;unsigned long long transportPackets=0,subtitlePackets=0;
  auto flush=[&](){if(!valid||pes.size()<14||pes[0]!=0||pes[1]!=0||pes[2]!=1)return;size_t header=9+pes[8];if(header>=pes.size())return;size_t length=(pes[4]<<8)|pes[5];if(length&&pes.size()>length+6)pes.resize(length+6);if(header>=pes.size()||(pes[7]&0x80)==0)return;int64_t stamp=pts(pes.data()+9);if(lastPts>=0&&stamp+((int64_t)1<<32)<lastPts)wrap+=(int64_t)1<<33;lastPts=stamp;out.Decode(pes.data()+header,pes.size()-header,(stamp+wrap)/90);};
  while(input.read((char*)p,188)) {
   if(p[0]!=0x47){input.seekg(-187,std::ios::cur);continue;}transportPackets++;if((((p[1]&31)<<8)|p[2])!=pid)continue;subtitlePackets++;
   if(p[1]&0x80||!(p[3]&0x10)){valid=false;continue;}int off=4;if(p[3]&0x20)off+=1+p[4];if(off>=188)continue;
   int cc=p[3]&15;if(last==cc)continue;if(last>=0&&cc!=((last+1)&15))valid=false;last=cc;
   if(p[1]&0x40){flush();pes.clear();valid=true;}if(valid){pes.insert(pes.end(),p+off,p+188);if(pes.size()>1024*1024){pes.clear();valid=false;}}
  }flush();out.Save(transportPackets,subtitlePackets);return 0;
 }catch(const std::exception& e){fprintf(stderr,"%s\n",e.what());return 1;}
}
