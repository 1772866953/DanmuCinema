import { useContext, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, useIsPresent } from 'motion/react'
import { Check, ChevronDown } from 'lucide-react'
import { AppearanceContext, GlassSurface, GlassButton } from './glass'


type Option = { value: string; label: string; color?: string }
type Placement = { left: number; top: number; width: number; maxHeight: number; above: boolean }

export function SelectControl({ id, label, value, options, onChange, disabled = false, className = '' }: {
  id?: string; label: string; value: string | number; options: readonly Option[]; onChange(value: string): void; disabled?: boolean; className?: string
}) {
  const generated = useId(), listId = `${generated}-options`
  const button = useRef<HTMLButtonElement>(null), popup = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false), [active, setActive] = useState(0)
  const [scrollTarget, setScrollTarget] = useState({ index: 0 })
  const [placement, setPlacement] = useState<Placement | null>(null)
  const prefix = useRef({ text: '', time: 0 })
  const selected = Math.max(0, options.findIndex((option) => option.value === String(value)))
  const close = (restore = false) => { setOpen(false); if (restore) button.current?.focus({ preventScroll: true }) }
  const choose = (index: number) => { onChange(options[index].value); close(true) }
  const activate = (index: number) => { setActive(index); setScrollTarget({ index }) }
  const reveal = () => { if (!disabled) { activate(selected); setOpen(true) } }
  useLayoutEffect(() => {
    if (!open) return
    const update = () => {
      const rect = button.current?.getBoundingClientRect()
      if (!rect) return
      const height = Math.min(options.length * 44 + Math.max(0, options.length - 1) * 24 + 66, 400)
      const below = innerHeight - rect.bottom - 14, above = rect.top - 48
      const up = below < height && above > below
      const maxHeight = Math.max(80, Math.min(height, up ? above : below))
      const width = Math.min(innerWidth - 24, Math.max(190, rect.width + 24))
      setPlacement({ left: Math.min(innerWidth - width - 12, Math.max(12, rect.left)), top: up ? rect.top - maxHeight - 6 : rect.bottom + 6, width, maxHeight, above: up })
    }
    const pointer = (event: PointerEvent) => { if (!button.current?.contains(event.target as Node) && !popup.current?.contains(event.target as Node)) close() }
    const scroll = (event: Event) => { if (!popup.current?.contains(event.target as Node)) update() }
    update(); window.addEventListener('resize', update); document.addEventListener('scroll', scroll, true); document.addEventListener('pointerdown', pointer)
    return () => { window.removeEventListener('resize', update); document.removeEventListener('scroll', scroll, true); document.removeEventListener('pointerdown', pointer) }
  }, [open, options.length])
  useEffect(() => { if (disabled) setOpen(false) }, [disabled])
  return <div className={`select-control ${className}`}>
    <GlassButton nativeLayout optics="clear" type="button" className="select-trigger" id={id || generated} ref={button} role="combobox" aria-label={label}
      aria-expanded={open} aria-controls={open ? listId : undefined} aria-haspopup="listbox" disabled={disabled}
      aria-activedescendant={open ? `${listId}-${active}` : undefined} onClick={() => open ? close() : reveal()}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && open) { event.preventDefault(); event.stopPropagation(); close(); return }
        if (event.key === 'Tab') { close(); return }
        if (['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' '].includes(event.key)) {
          event.preventDefault(); event.stopPropagation()
          if (!open) { reveal(); return }
          if (event.key === 'Enter' || event.key === ' ') { choose(active); return }
          activate(event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1 : (active + (event.key === 'ArrowDown' ? 1 : -1) + options.length) % options.length)
        } else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey) {
          const now = Date.now(); prefix.current.text = now - prefix.current.time < 700 ? prefix.current.text + event.key : event.key; prefix.current.time = now
          const index = options.findIndex((option) => option.label.toLocaleLowerCase().startsWith(prefix.current.text.toLocaleLowerCase()))
          if (index >= 0) { if (!open) reveal(); activate(index) }
        }
      }}><span className="select-option-label">{options[selected]?.color && <i className="select-color-dot" style={{ background: options[selected].color }} />}{options[selected]?.label}</span><ChevronDown size={15} /></GlassButton>
    {createPortal(<AnimatePresence>{open && placement && <SelectPopup key={listId} popup={popup} id={listId} label={label} placement={placement}
      options={options} selected={selected} active={active} scrollTarget={scrollTarget} onActive={setActive} onChoose={choose} />}</AnimatePresence>, document.body)}
  </div>
}

function SelectPopup({ popup, id, label, placement, options, selected, active, scrollTarget, onActive, onChoose }: {
  popup: React.RefObject<HTMLDivElement | null>; id: string; label: string; placement: Placement; options: readonly Option[];
  selected: number; active: number; scrollTarget: { index: number }; onActive(index: number): void; onChoose(index: number): void
}) {
  const present = useIsPresent(), { reduced } = useContext(AppearanceContext)
  useLayoutEffect(() => {
    const list = popup.current?.querySelector<HTMLElement>('[role="listbox"]')
    const option = list?.querySelector<HTMLElement>(`[data-index="${scrollTarget.index}"]`)
    if (!list || !option) return
    // Only opening and keyboard navigation scroll the list. Pointer hover must
    // not move a partially visible row or scroll any of the popup's ancestors.
    const top = option.offsetTop - list.offsetTop, bottom = top + option.offsetHeight
    if (top < list.scrollTop) list.scrollTop = top
    else if (bottom > list.scrollTop + list.clientHeight) list.scrollTop = bottom - list.clientHeight
  }, [popup, scrollTarget])
  return <div ref={popup} className="select-popup" style={{ left: placement.left, top: placement.top, width: placement.width }}
    inert={!present || undefined} aria-hidden={!present || undefined}>
    <GlassSurface kind="popover" radius={18} className="select-menu" initial={{ opacity: 0, y: reduced ? 0 : placement.above ? 4 : -4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }} transition={{ duration: reduced ? .07 : .18 }}><div role="listbox" id={id} aria-label={label} style={{ maxHeight: Math.max(44, placement.maxHeight - 22) }}>
      {options.map((option, index) => <GlassButton nativeLayout optics="inherited" type="button" key={option.value} role="option" id={`${id}-${index}`} data-index={index} tabIndex={-1}
        aria-selected={index === selected} className={index === active ? 'is-active' : ''} onPointerMove={() => onActive(index)}
        onPointerDown={(event) => event.preventDefault()} onClick={() => onChoose(index)}><span className="select-option-label">{option.color && <i className="select-color-dot" style={{ background: option.color }} />}{option.label}</span>{selected === index && <Check size={15} />}</GlassButton>)}
    </div></GlassSurface>
  </div>
}

