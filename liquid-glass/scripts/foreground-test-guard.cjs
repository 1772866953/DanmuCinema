'use strict'
// Visible tests can steal focus or compete with the user's GPU workload.
// Headless/null-output and QA offscreen tests do not use this guard.
module.exports=function requireForegroundTest(){
 if(process.env.DANMU_ALLOW_FOREGROUND!=='1')throw new Error('前台性能测试未启用。获得本次前台测试授权后，设置 DANMU_ALLOW_FOREGROUND=1 再运行；后台工作请使用 headless/offscreen 脚本。')
}
