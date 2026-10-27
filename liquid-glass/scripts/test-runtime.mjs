import {createRequire} from 'node:module'
import {homedir} from 'node:os'
import path from 'node:path'

const local = createRequire(import.meta.url)
const shared = createRequire(path.join(homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/resolver.cjs'))
export function runtime(name) {
  try { return local(name) } catch (error) {
    if (error.code !== 'MODULE_NOT_FOUND') throw error
    return shared(name)
  }
}
