// Windows default content ID 0 is authoritative default-rights state. Query
// DRMK through a temporary mixed ID; no failed/unknown query grants export.
#pragma once
namespace harmonia::contentrights {
template<class Rights>
constexpr bool Clear(const Rights& rights) noexcept {
    return !rights.Reserved && !rights.CopyProtect && !rights.DigitalOutputDisable;
}
template<class Port,class Id,class Rights>
auto Query(Port* port,Id content,Rights* rights) -> decltype(port->GetContentRights(content,rights)) {
    using Status=decltype(port->GetContentRights(content,rights));
    constexpr Status denied=static_cast<Status>(-1073741790); // STATUS_ACCESS_DENIED
    if(!rights)return denied;
    *rights={};
    if(!port)return denied;
    if(content)return port->GetContentRights(content,rights);
    Id derived=0;
    Status status=port->CreateContentMixed(&content,1,&derived);
    if(status<0)return status;
    if(!derived)return denied;
    status=port->GetContentRights(derived,rights);
    port->DestroyContent(derived);
    return status;
}
}
