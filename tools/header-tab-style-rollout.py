"""Moves hand-written headings, tabs, choice buttons and square icon buttons to the shared styles
(docs/UI_STYLE.md): page-title, block-title, section-label, segmented, choice and icon-btn.

Usage: python tools/header-tab-style-rollout.py src/DigiByte.Web [--apply]
Without --apply it only reports what it would do.
"""
import collections
import glob
import os
import re
import sys

root = sys.argv[1]
apply = '--apply' in sys.argv
stats = collections.Counter()
skipped = collections.Counter()

LAYOUT = re.compile(r'^(flex-1|flex-none|flex-shrink-0|shrink-0|grow|w-full|self-\w+|col-span-\d+|-?m[trblxy]?-[\w.\[\]]+|'
                    r'px-1|slide-up(-delay-\d)?|fade-in|scale-in|text-center|text-left|text-right|truncate|break-words|break-all|'
                    r'min-w-0|max-w-\S+|sm:\S+|leading-\S+|whitespace-nowrap)$')

# ---------------------------------------------------------------- headings
HEADING = re.compile(r'<(h[2-4])\b([^<>]*?)\bclass="([^"@]*)"')
H_DROP = re.compile(r'^(text-(xl|lg|base|sm|xs|\[1[01]px\])|font-(bold|semibold)|text-slate-(900|500|400)|'
                    r'dark:text-(white|slate-400|slate-500)|uppercase|tracking-(wider|widest))$')


def heading(m):
    tag, pre, cls = m.group(1), m.group(2), m.group(3)
    names = cls.split()
    s = set(names)
    if s & {'page-title', 'block-title', 'section-label'}:
        return m.group(0)
    kind = None
    if 'uppercase' in s and (s & {'tracking-wider', 'tracking-widest'}):
        kind = ['section-label']
    elif 'text-slate-900' in s and 'dark:text-white' in s and (s & {'font-bold', 'font-semibold'}):
        if 'text-xl' in s and tag == 'h2':
            kind = ['page-title']
        elif 'text-lg' in s:
            kind = ['block-title', 'block-title-lg']
        elif 'text-base' in s:
            kind = ['block-title']
        elif 'text-sm' in s:
            kind = ['block-title', 'block-title-sm']
    if kind is None:
        return m.group(0)
    kept = [x for x in names if not H_DROP.match(x)]
    if any(not LAYOUT.match(x) for x in kept):
        skipped['heading with other classes'] += 1
        return m.group(0)
    stats[kind[0]] += 1
    return '<%s%sclass="%s"' % (tag, pre, ' '.join(kind + kept))


# ---------------------------------------------------------------- tabs: the container
SEG_BOX = re.compile(r'<div\b([^<>]*?)\bclass="([^"@]*)"')
SEG_DROP = re.compile(r'^(flex|bg-slate-100|dark:bg-dgb-\d+|rounded-xl|p-1|gap-0\.5|text-sm|font-semibold)$')


def seg_box(m):
    names = m.group(2).split()
    s = set(names)
    if not ({'flex', 'bg-slate-100', 'rounded-xl', 'p-1'} <= s):
        return m.group(0)
    kept = [x for x in names if not SEG_DROP.match(x)]
    if any(not LAYOUT.match(x) for x in kept):
        return m.group(0)
    stats['segmented'] += 1
    return '<div%sclass="%s"' % (m.group(1), ' '.join(['segmented'] + kept))


# ---------------------------------------------------------------- tabs and choices: the buttons
TOGGLE = re.compile(r'(?P<head><button\b(?:"[^"]*"|\'[^\']*\'|[^<>"\'])*?)\bclass="(?P<pre>[^"@]*)@\((?P<cond>[^?"]+?)\s*\?\s*"(?P<on>[^"]*)"\s*:\s*"(?P<off>[^"]*)"\)(?P<post>[^"]*)"', re.S)


def toggle(m):
    pre, on, off, post = m.group('pre').split(), m.group('on'), m.group('off'), m.group('post').strip()
    s = set(pre)
    if post or 'aria-pressed' in m.group('head') or 'role="switch"' in m.group('head'):
        return m.group(0)
    if not (s & {'text-xs', 'text-sm'}) or not (s & {'rounded-lg', 'rounded-xl', 'rounded-full'}):
        return m.group(0)
    if re.search(r'\b(w|h)-\d+\b', ' '.join(pre)):
        return m.group(0)  # switches and swatches
    if 'bg-white' in on and 'shadow' in on:
        kind = ['segmented-item']
    elif on.startswith('bg-dgb-500 text-white') and len(on.split()) == 2:
        kind = ['choice'] + (['choice-pill'] if 'rounded-full' in s else [])
    elif on.startswith('border-dgb-500'):
        kind = ['choice']
    else:
        skipped['toggle with a look of its own'] += 1
        return m.group(0)
    kept = [x for x in pre if x in ('flex-1', 'w-full', 'flex-shrink-0', 'shrink-0')]
    stats[kind[0]] += 1
    cond = m.group('cond').strip()
    return '%sclass="%s" aria-pressed="@(%s ? "true" : "false")"' % (m.group('head'), ' '.join(kind + kept), cond)


# ---------------------------------------------------------------- square icon buttons
ICON = re.compile(r'<(button|a)\b((?:"[^"]*"|\'[^\']*\'|[^<>"\'])*?)\bclass="([^"@]*)"')
I_DROP = re.compile(r'^((w|h)-(8|9|10|11)|rounded-(lg|xl|2xl|full)|bg-\S+|dark:\S+|hover:\S+|border(-\S+)?|flex|inline-flex|items-center|'
                    r'justify-center|transition(-\w+)?|duration-\d+|press-effect|shadow(-\S+)?|text-white|text-slate-\d+|text-dgb-\d+|p-\d(\.5)?)$')


def icon(m):
    names = m.group(3).split()
    s = set(names)
    size = [x for x in names if re.match(r'^w-(8|9|10|11)$', x)]
    if not size or ('h-' + size[0][2:]) not in s:
        return m.group(0)
    if not ({'items-center', 'justify-center'} <= s) or not any(x.startswith('rounded-') for x in names):
        return m.group(0)
    if any(x.startswith('btn-') or x == 'icon-btn' for x in names):
        return m.group(0)
    if s & {'absolute', 'fixed', 'text-left'} or any(re.match(r'^bg-(red|amber|green|emerald|purple|indigo|orange)-', x) for x in names):
        skipped['icon button that is positioned or has a colour of its own'] += 1
        return m.group(0)
    kept = [x for x in names if not I_DROP.match(x)]
    if any(not LAYOUT.match(x) for x in kept):
        skipped['icon button with other classes'] += 1
        return m.group(0)
    kind = ['icon-btn']
    if 'bg-dgb-500' in s:
        kind.append('icon-btn-primary')
    if size[0] == 'w-8':
        kind.append('icon-btn-sm')
    stats['icon-btn-primary' if 'bg-dgb-500' in s else 'icon-btn'] += 1
    return '<%s%sclass="%s"' % (m.group(1), m.group(2), ' '.join(kind + kept))


files = [f for pat in ('Pages/**/*.razor', 'Components/**/*.razor', 'Layout/**/*.razor')
         for f in glob.glob(os.path.join(root, pat), recursive=True)]
changed = 0
for path in sorted(files):
    rel = os.path.relpath(path, root).replace('\\', '/')
    if rel.startswith('Components/Dev/') or rel.endswith('TestAnimations.razor') or rel.endswith('DevNode.razor'):
        continue
    src = open(path, encoding='utf-8').read()
    out = HEADING.sub(heading, src)
    out = SEG_BOX.sub(seg_box, out)
    out = TOGGLE.sub(toggle, out)
    out = ICON.sub(icon, out)
    if out != src:
        changed += 1
        if apply:
            open(path, 'w', encoding='utf-8', newline='').write(out)

print('converted:', dict(stats), 'in', changed, 'files')
print('skipped:', dict(skipped))
