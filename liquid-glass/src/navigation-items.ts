import {LayoutDashboard,FolderOpen,Download,Monitor,SlidersHorizontal,Clock3,Database,ScrollText} from 'lucide-react'
export const navigationPages=[['overview','服务总览',LayoutDashboard],['library','影片与弹幕',FolderOpen],['tasks','下载任务',Download],['connect','设备与媒体库',Monitor],['settings','启动与偏好',SlidersHorizontal],['schedule','定时任务',Clock3],['cache','缓存管理',Database],['logs','运行日志',ScrollText]] as const
export const navigationItems=navigationPages.map(([id,label,Icon])=>({id,label,Icon}))
