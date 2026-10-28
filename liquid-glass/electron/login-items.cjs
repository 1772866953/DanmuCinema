'use strict'

// Electron reads openAtLogin using the AppUserModelID, even when the setter
// receives a custom registry name. Keep both names identical to the installer.
function createLoginItems(app,{path,args,name='DanmuCinema'}){
  app.setAppUserModelId(name)
  const options={path,args:[...args]}
  function get(){
    const status=app.getLoginItemSettings(options)
    const entry=status.launchItems.find(item=>item.scope==='user'&&item.name===name)
    return Boolean(status.openAtLogin&&entry?.enabled)
  }
  function set(value){
    const enabled=Boolean(value)
    app.setLoginItemSettings({...options,name,openAtLogin:enabled,enabled})
    const actual=get()
    if(actual!==enabled)throw new Error('开机启动设置未生效，请检查系统启动项权限。')
    return actual
  }
  return {get,set}
}
module.exports={createLoginItems}
