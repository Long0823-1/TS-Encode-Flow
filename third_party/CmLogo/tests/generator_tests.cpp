// Regression tests for the GPL-3.0 standalone logo generator.
#define HAVE_AVISYNTH 0
#define HAVE_DTVINDEX 0
#include "../src/logo_generator.cpp"
#include <iostream>

static int checks=0;
static void check(bool ok,const char *name){if(!ok)throw std::runtime_error(name);++checks;std::cout<<"PASS "<<name<<std::endl;}
class Fixture:public VideoSource {
 InputVideoInfo vi={32,32,30,1,120,false,false};std::vector<unsigned char> pixels;
public:
 bool logo=true,text=false;
 const InputVideoInfo &info()const override{return vi;}
 const char *source_name()const override{return "fixture";}
 InputVideoFrame read_frame(int n)override {
  int bg=30+(n*17)%160;pixels.assign(32*32,static_cast<unsigned char>(bg));
  if(logo)for(int y=10;y<18;y++)for(int x=10;x<18;x++)pixels[y*32+x]=static_cast<unsigned char>((bg*3+230)/4);
  if(text)for(int y=5;y<11;y++)pixels[y*32+4]=0;
  return {pixels.data(),32};
 }
};
int main(int argc,char **argv){try {
 if(argc!=2)throw std::runtime_error("output directory required");std::string root=argv[1];
 Fixture f;LogoRect rect(4,4,24,24);int bg=0;
 check(estimate_background(f.read_frame(0),rect,12,&bg)&&bg==30,"strict flat border remains unchanged");
 f.text=true;check(!estimate_background(f.read_frame(0),rect,12,&bg),"legacy strict test rejects edge text");
 check(estimate_background(f.read_frame(0),rect,12,&bg,true)&&bg==30,"dominant border recovers background with small edge text");
 std::vector<unsigned char> split(32*32,30);for(int y=0;y<32;y++)for(int x=16;x<32;x++)split[y*32+x]=200;
 InputVideoFrame mixed={split.data(),32};check(!estimate_background(mixed,rect,12,&bg,true),"two large background regions are not treated as uniform");
 ManagedLogoOptions o;o.has_manual_rect=true;o.manual_rect=rect;o.sample_frames=120;o.channel="fixture";
 auto generated=generate_logo(f,o,0,120,root+"/with-text.lgd");check(generated.active_pixels>=60&&generated.logo_frames>=8,"full generation recovers translucent logo despite edge text");
 f.text=false;auto clean=generate_logo(f,o,0,120,root+"/clean.lgd");check(clean.active_pixels>=60,"ordinary clean logo still generates");
 f.logo=false;bool rejected=false;try{generate_logo(f,o,0,120,root+"/no-logo.lgd");}catch(const std::runtime_error&){rejected=true;}check(rejected,"no-logo video is rejected instead of generating an empty logo");
 std::cout<<"ALL PASS "<<checks<<std::endl;return 0;
 }catch(const std::exception &e){std::cerr<<e.what()<<std::endl;return 1;}}
