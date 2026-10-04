import { useEffect, useRef, useState } from 'react';
import { ArrowUpRight, X } from '@phosphor-icons/react';
export default function CreateSaveDialog({ number, onCreate, onCancel }) {
  const dialog = useRef(null), input = useRef(null);
  const [name, setName] = useState(`新的故事 ${String(number).padStart(2, '0')}`), [error, setError] = useState('');
  useEffect(() => {
    const el = dialog.current, previous = document.activeElement;
    el.showModal(); input.current.focus(); input.current.select();
    return () => { el.close(); previous?.focus({ preventScroll: true }); };
  }, []);
  const submit = event => {
    event.preventDefault();
    if (!name.trim()) { setError('请为这份存档起一个名字。'); input.current.focus(); return; }
    onCreate(name.trim());
  };
  return <dialog ref={dialog} className="create-save-dialog" aria-labelledby="create-title" aria-describedby="create-description"
    onCancel={event => { event.preventDefault(); onCancel(); }}>
    <button className="dialog-close" aria-label="取消新建存档" onClick={onCancel}><X size={22} /></button>
    <span className="slot-kicker">A NEW BEGINNING / {String(number).padStart(2, '0')}</span>
    <h2 id="create-title">新的故事，从这里开始。</h2><p id="create-description">为你的战斗记录命名。</p>
    <form onSubmit={submit}>
      <label htmlFor="save-name">存档名称<span>{name.length} / 24</span></label>
      <input ref={input} id="save-name" name="save-name" type="text" maxLength={24} value={name} autoComplete="off"
        aria-invalid={!!error} aria-describedby={error ? 'save-name-error' : undefined}
        onChange={event => { setName(event.target.value); setError(''); }} />
      <p className="save-name-error" id="save-name-error" role="status">{error || '\u00a0'}</p>
      <div className="dialog-actions"><button type="button" className="cancel-create" onClick={onCancel}>取消</button>
        <button type="submit" className="confirm-button">创建存档<ArrowUpRight size={22} /></button></div>
    </form><p className="local-save-note">保存在当前浏览器，用于菜单交互预览。</p>
  </dialog>;
}
