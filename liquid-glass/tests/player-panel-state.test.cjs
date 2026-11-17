const {test}=require('node:test'),assert=require('node:assert/strict')
test('routine player telemetry leaves the settings form stable while diagnostics still update',async()=>{
 const {playerPanelSignature:form,playerDiagnosticSignature:diagnostics}=await import('../src/player-panel-state.ts')
 const initial={active:true,title:'test',settings:{danmuFps:0,fontSize:28,volume:60,speed:1},video:{w:3840,h:2160,primaries:'bt.709'},target:{primaries:'bt.709',gamma:'bt.1886'},'frame-drop-count':0}
 for(let n=0;n<100;n++)assert.equal(form({...initial,position:n,pause:n%2===0,speed:2,avsync:.001*n,'frame-drop-count':n,settings:{...initial.settings,volume:n,speed:2}}),form(initial))
 assert.notEqual(diagnostics({...initial,'frame-drop-count':1}),diagnostics(initial));assert.notEqual(diagnostics({...initial,video:{...initial.video,w:1920}}),diagnostics(initial))
})
test('all settings, playback availability and offset changes reach the form',async()=>{
 const {playerPanelSignature:form}=await import('../src/player-panel-state.ts'),initial={active:true,starting:false,title:'first',settings:{danmuFps:0,fontSize:28}}
 for(const [key,value]of Object.entries({danmuFps:60,fontSize:32,subtitleColor:'#ff0088',uiFontSize:18,newSetting:true}))assert.notEqual(form({...initial,settings:{...initial.settings,[key]:value}}),form(initial))
 for(const [key,value]of Object.entries({active:false,starting:true,title:'second',error:'failed',notice:'notice',danmu:'ready','audio-delay':.5,'sub-delay':-.5}))assert.notEqual(form({...initial,[key]:value}),form(initial))
})
