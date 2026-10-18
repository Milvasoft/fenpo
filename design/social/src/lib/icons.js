// Icon sprite copied from design/desktop/desktop.html.
window.ICONS = `<svg width="0" height="0" style="position:absolute" aria-hidden="true">
  <defs>
    <symbol id="logo" viewBox="0 0 64 64">
      <rect x="3" y="3" width="58" height="58" rx="19" fill="#E8577E"/>
      <circle cx="32" cy="32" r="21" fill="#FFF6F0"/>
      <g fill="#1B1F3B">
        <path id="seed" d="M32 15.5c3.4 4.2 3.7 9 0 11.6c-3.7-2.6-3.4-7.4 0-11.6z"/>
        <use href="#seed" transform="rotate(72 32 32)"/><use href="#seed" transform="rotate(144 32 32)"/>
        <use href="#seed" transform="rotate(216 32 32)"/><use href="#seed" transform="rotate(288 32 32)"/>
      </g>
    </symbol>
    <symbol id="i-phone" viewBox="0 0 24 24"><rect x="7" y="2.5" width="10" height="19" rx="2.5"/><path d="M10.5 5.5h3"/></symbol>
    <symbol id="i-bell" viewBox="0 0 24 24"><path d="M6 10a6 6 0 0 1 12 0v4l1.5 3h-15L6 14z"/><path d="M10 20a2 2 0 0 0 4 0"/></symbol>
    <symbol id="i-chat" viewBox="0 0 24 24"><path d="M4 5.5A1.5 1.5 0 0 1 5.5 4h13A1.5 1.5 0 0 1 20 5.5v9a1.5 1.5 0 0 1-1.5 1.5H9l-5 4z"/></symbol>
    <symbol id="i-call" viewBox="0 0 24 24"><path d="M5 4h3.5l2 5-2.5 1.5a11 11 0 0 0 5.5 5.5L15 13.5l5 2V19a1 1 0 0 1-1 1A16 16 0 0 1 4 5a1 1 0 0 1 1-1z"/></symbol>
    <symbol id="i-folder" viewBox="0 0 24 24"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2h8.5A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z"/></symbol>
    <symbol id="i-clip" viewBox="0 0 24 24"><rect x="8.5" y="3" width="7" height="3.5" rx="1"/><path d="M8.5 5H6.5A1.5 1.5 0 0 0 5 6.5v13A1.5 1.5 0 0 0 6.5 21h11a1.5 1.5 0 0 0 1.5-1.5v-13A1.5 1.5 0 0 0 17.5 5h-2"/><path d="M9 12h6M9 16h4"/></symbol>
    <symbol id="i-cam" viewBox="0 0 24 24"><path d="M3 8.5A1.5 1.5 0 0 1 4.5 7h2.5l2-2.5h6L17 7h2.5A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z"/><circle cx="12" cy="12.5" r="3.5"/></symbol>
    <symbol id="i-gear" viewBox="0 0 24 24"><circle cx="12" cy="12" r="3"/><path d="M12 2.5v2.5M12 19v2.5M2.5 12H5M19 12h2.5M5.3 5.3l1.8 1.8M16.9 16.9l1.8 1.8M5.3 18.7l1.8-1.8M16.9 7.1l1.8-1.8"/></symbol>
    <symbol id="i-search" viewBox="0 0 24 24"><circle cx="11" cy="11" r="6"/><path d="M20 20l-4.5-4.5"/></symbol>
    <symbol id="i-ring" viewBox="0 0 24 24"><path d="M4 10v4h4l5 4V6L8 10z"/><path d="M16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11"/></symbol>
    <symbol id="i-hotspot" viewBox="0 0 24 24"><path d="M4.5 9a11 11 0 0 1 15 0M7.5 12.5a6.5 6.5 0 0 1 9 0M10.5 16a2.2 2.2 0 0 1 3 0"/><circle cx="12" cy="19" r=".8"/></symbol>
    <symbol id="i-send" viewBox="0 0 24 24"><path d="M4 12l16-8-6 16-2.5-6.5z"/><path d="M11.5 13.5L20 4"/></symbol>
    <symbol id="i-mirror" viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M9 20h6M12 16v4"/></symbol>
    <symbol id="i-bt" viewBox="0 0 24 24"><path d="M7 7l10 10-5 4V3l5 4L7 17"/></symbol>
    <symbol id="i-wifi" viewBox="0 0 24 24"><path d="M3.5 9.5a12 12 0 0 1 17 0M6.5 13a7.5 7.5 0 0 1 11 0M9.5 16.5a3.2 3.2 0 0 1 5 0"/><circle cx="12" cy="19.5" r=".6"/></symbol>
    <symbol id="i-usb" viewBox="0 0 24 24"><path d="M12 3v14M9 6l3-3 3 3M12 17a2 2 0 1 0 0 4 2 2 0 0 0 0-4M12 13l-4-2V8M12 11l4-2V7"/></symbol>
    <symbol id="i-lock" viewBox="0 0 24 24"><rect x="5" y="11" width="14" height="9.5" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></symbol>
    <symbol id="i-moon" viewBox="0 0 24 24"><path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/></symbol>
    <symbol id="i-link" viewBox="0 0 24 24"><path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/></symbol>
    <symbol id="i-play" viewBox="0 0 24 24"><path d="M8 5.5v13l11-6.5z"/></symbol>
    <symbol id="i-pause" viewBox="0 0 24 24"><rect x="6.5" y="5" width="4" height="14" rx="1"/><rect x="13.5" y="5" width="4" height="14" rx="1"/></symbol>
    <symbol id="i-next" viewBox="0 0 24 24"><path d="M5 6v12l8.5-6zM13.5 6v12l8.5-6z"/></symbol>
    <symbol id="i-prev" viewBox="0 0 24 24"><path d="M19 6v12l-8.5-6zM10.5 6v12L2 12z"/></symbol>
    <symbol id="i-x" viewBox="0 0 24 24"><path d="M6 6l12 12M18 6L6 18"/></symbol>
    <symbol id="i-check" viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5"/></symbol>
    <symbol id="i-down" viewBox="0 0 24 24"><path d="M12 4v12M7 11l5 5 5-5M5 20h14"/></symbol>
    <symbol id="i-copy" viewBox="0 0 24 24"><rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5.5A1.5 1.5 0 0 0 14.5 4h-9A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16H8"/></symbol>
    <symbol id="i-pc" viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="12" rx="1.5"/><path d="M8 20h8"/></symbol>
    <symbol id="i-hangup" viewBox="0 0 24 24"><path d="M3 14.5c5-5 13-5 18 0l-2.5 2.5-3.5-1.5v-3a10 10 0 0 0-6 0v3L5.5 17z"/></symbol>
    <symbol id="i-minus" viewBox="0 0 24 24"><path d="M6 12h12"/></symbol>
    <symbol id="i-square" viewBox="0 0 24 24"><rect x="6" y="6" width="12" height="12" rx="1"/></symbol>
    <symbol id="i-plus" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14"/></symbol>
    <symbol id="i-sun" viewBox="0 0 24 24"><circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/></symbol>
    <symbol id="i-globe" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9S9.5 5.6 12 3z"/></symbol>
    <symbol id="i-focus" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="3"/></symbol>
  </defs>
</svg>
`;
