#include "../native/ContentRightsPolicy.h"
#include <iostream>
#include <stdexcept>
#include <string>
struct Rights { bool CopyProtect=false;unsigned Reserved=0;bool DigitalOutputDisable=false; };
struct Port {
    long createStatus=0,queryStatus=0;unsigned derived=42,queried=0,destroyed=0,created=0;
    Rights returned{};
    long CreateContentMixed(unsigned* content,unsigned count,unsigned* output) {
        if(count!=1 || *content!=0)throw std::runtime_error("default content input changed");
        ++created;*output=derived;return createStatus;
    }
    long GetContentRights(unsigned id,Rights* rights) {queried=id;*rights=returned;return queryStatus;}
    long DestroyContent(unsigned id) {destroyed=id;return 0;}
};
static unsigned checks=0;
static void Check(bool ok){++checks;if(!ok)throw std::runtime_error("rights check "+std::to_string(checks));}
int main() try {
    using namespace harmonia::contentrights;
    Rights rights{};Port port;
    Check(Query(&port,0U,&rights)==0 && Clear(rights));
    Check(port.created==1 && port.queried==42 && port.destroyed==42);
    port={};Check(Query(&port,7U,&rights)==0 && port.created==0 && port.queried==7 && port.destroyed==0);
    port={};port.createStatus=-1;Check(Query(&port,0U,&rights)<0 && port.queried==0 && port.destroyed==0);
    port={};port.derived=0;Check(Query(&port,0U,&rights)<0 && port.queried==0 && port.destroyed==0);
    port={};port.queryStatus=-1;Check(Query(&port,0U,&rights)<0 && port.destroyed==42);
    for(unsigned flags=0;flags<8;++flags) {
        port={};port.returned.CopyProtect=(flags&1)!=0;port.returned.Reserved=flags&2;port.returned.DigitalOutputDisable=(flags&4)!=0;
        Check(Query(&port,0U,&rights)==0);Check(Clear(rights)==(flags==0));Check(port.destroyed==42);
    }
    Check(Query(static_cast<Port*>(nullptr),0U,&rights)<0);
    Check(Query(&port,0U,static_cast<Rights*>(nullptr))<0);
    std::cout<<"PASS: "<<checks<<" default/explicit content rights checks; failures denied and derived IDs released\n";return 0;
} catch(const std::exception& e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
