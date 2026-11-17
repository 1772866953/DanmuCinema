import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import fs from 'node:fs'
// A small ASS overlay does not benefit from eight pthreads per canvas. Keep
// the upstream files and WASM unchanged; this build policy also avoids spawning
// idle Emscripten workers. Its recipe is included in the corresponding source.
function assThreadPolicy(){return{name:'cinema-ass-overlay-policy',transform(code:string,id:string){
 const file=id.replace(/\\/g,'/')
 if(process.env.VITE_DANMU_ASS_SERIAL!=='0'&&file.endsWith('/jassub/dist/worker/util.js')){const declaration=/export const THREAD_COUNT = [^;]+;/;if(!declaration.test(code))throw Error('ASS worker thread policy needs review after dependency update');return code.replace(declaration,'export const THREAD_COUNT = 1;')}
 if(process.env.VITE_DANMU_ASS_ASYNC_GPU!=='0'&&file.endsWith('/jassub/dist/worker/renderers/webgl2-renderer.js')){if(code.split('this.gl.finish();').length!==2)throw Error('ASS GPU queue policy needs review after dependency update');const upload='this.gl.bufferSubData(this.gl.ARRAY_BUFFER, 0, this.instanceBytes, 0, instanceCount * INSTANCE_STRIDE);';if(!code.includes(upload))throw Error('ASS instance-buffer policy needs review after dependency update');let output=code.replace('this.gl.finish();','/* Submission fences are bounded by cinema GPUQueue. */').replace(upload,'this.gl.bufferData(this.gl.ARRAY_BUFFER, this.instanceCapacity * INSTANCE_STRIDE, this.gl.STREAM_DRAW);\n'+upload);if(process.env.VITE_DANMU_ASS_COMPOSITED!=='0'){if(output.split('desynchronized: true').length!==2)throw Error('ASS compositing policy needs review after dependency update');output=output.replace('desynchronized: true','desynchronized: false')}return output}
}}}
const assRoot=path.resolve(fs.existsSync('node_modules/jassub/package.json')?'node_modules/jassub':'development/dependencies/ass-overlay/node_modules/jassub')
const alias=[{find:/^jassub$/,replacement:path.join(assRoot,'dist/jassub.js')},{find:/^jassub\//,replacement:assRoot+'/'},{find:'rvfc-polyfill',replacement:path.resolve('player-overlay/no-video-polyfill.js')}]
export default defineConfig({ base:'./',plugins:[react(),assThreadPolicy()],resolve:{alias},worker:{format:'es',plugins:()=>[assThreadPolicy()]},build:{chunkSizeWarningLimit:1000,rollupOptions:{input:[path.resolve('index.html'),path.resolve('player-overlay/index.html')]}} })
