"""Moves hand-written text fields and card blocks to the shared .input and .card styles (docs/UI_STYLE.md).

Usage: python tools/input-card-style-rollout.py src/DigiByte.Web [inputs|cards] [--apply]
Without --apply it only reports what it would do.

It converts only the plain, standard cases and reports what it leaves alone. The shared classes load after
tailwind.css, so a Tailwind utility for a property they set (colour, border, radius, padding, width of a field)
would be dead weight; anything relying on such a utility is skipped instead of being changed.
"""
import collections
import glob
import os
import re
import sys

root = sys.argv[1]
what = sys.argv[2]
apply = '--apply' in sys.argv

stats = collections.Counter()
skipped = collections.Counter()
unknown = collections.Counter()
changed = []

LAYOUT = (r'flex-1|flex-none|flex-shrink-0|shrink-0|grow|self-\w+|col-span-\d+|order-\d+|-?m[trblxy]?-[\w.\[\]]+|'
          r'slide-up(-delay-\d)?|fade-in|scale-in|max-w-\S+|min-w-\S+|ml-auto|mr-auto|mx-auto|relative|group|z-\d+|'
          r'space-[xy]-[\d.]+|gap-[\d.]+|flex|flex-col|flex-row|flex-wrap|items-\w+|justify-\w+|grid|grid-cols-\d+|'
          r'overflow-\S+|text-center|text-left|text-right|truncate|min-h-\S+|max-h-\S+|h-\S+|w-full|block|divide-\S+')

# ---------------------------------------------------------------- inputs
FIELD_TAG = re.compile(r'<(input|textarea)\b([^<>]*?(?:"[^"]*"[^<>]*?)*?)(/?)>', re.S)
FIELD_KEEP = re.compile(r'^(' + LAYOUT + r'|font-mono|uppercase|lowercase|tracking-\S+|resize-none|resize-y|break-all|tabular-nums)$')
FIELD_DROP = re.compile(r'^(w-full|block|bg-\S+|dark:\S+|hover:\S+|focus:\S+|focus-visible:\S+|disabled:\S+|placeholder:\S+|placeholder-\S+|'
                        r'text-(sm|base|xs|\[\d+px\]|slate-\d+|dgb-\d+|white)|rounded(-\w+)?|border(-\S+)?|p[xy]?-[\d.]+|'
                        r'outline-none|transition(-\w+)?|duration-\d+|shadow(-\S+)?|ring-\S+|leading-\S+|min-h-\[\d+px\])$')


def convert_field(tag, attrs):
    t = re.search(r'\btype="([^"]*)"', attrs)
    typ = t.group(1) if t else ''
    if typ in ('checkbox', 'radio', 'file', 'range', 'hidden', 'color'):
        return None
    cm = re.search(r'\bclass="([^"]*)"', attrs)
    if not cm:
        return None
    cls = cm.group(1)
    names = cls.split()
    if 'input' in names:
        return None
    if '@' in cls:
        skipped['class built with a Razor expression'] += 1
        return None
    if 'bg-transparent' in names:
        skipped['inside a composite field (amount box, search box)'] += 1
        return None
    if re.search(r'\b(sr-only|hidden|peer)\b', cls):
        return None
    if re.search(r'\bp[lr]-(8|9|10|11|12|14|16)\b', cls):
        skipped['padding reserved for an icon inside the field'] += 1
        return None
    if re.search(r'\bw-(\d+|\[)', cls) or 'text-center' in names:
        skipped['fixed width or centred text (PIN and code boxes)'] += 1
        return None
    if 'border' not in names or not re.search(r'\brounded-(lg|xl|2xl)\b', cls):
        skipped['not a standard bordered field'] += 1
        return None
    kept, bad = [], []
    for x in names:
        if FIELD_DROP.match(x):
            continue
        if FIELD_KEEP.match(x):
            kept.append(x)
        else:
            bad.append(x)
    if bad:
        skipped['unrecognised classes'] += 1
        unknown.update(bad)
        return None
    stats[tag] += 1
    return attrs[:cm.start(1)] + ' '.join(['input'] + kept) + attrs[cm.end(1):]


# ---------------------------------------------------------------- cards
CARD_TAG = re.compile(r'<(div|section|article|li|a|button)\b([^<>]*?(?:"[^"]*"[^<>]*?)*?)>', re.S)
CARD_KEEP = re.compile(r'^(' + LAYOUT + r'|p[xytblr]?-[\d.]+|sm:\S+|cursor-pointer|select-none|no-underline|card-hover|press-effect)$')
# Only the standard surface: white / dgb-900, slate-200 / dgb-800 border, 2xl or 3xl corners.
CARD_DROP = re.compile(r'^(bg-white|dark:bg-dgb-900|rounded-(2xl|3xl)|border|border-slate-200(/80)?|dark:border-dgb-800|shadow-sm|'
                       r'transition(-all|-colors|-shadow)?|duration-\d+)$')
CARD_NEEDS = ['bg-white', 'dark:bg-dgb-900', 'border']
HOVER = re.compile(r'^(hover:bg-slate-50|dark:hover:bg-dgb-800|hover:border-dgb-300|dark:hover:border-dgb-600|hover:shadow-\S+|active:\S+)$')


def convert_card(tag, attrs):
    cm = re.search(r'\bclass="([^"]*)"', attrs)
    if not cm:
        return None
    cls = cm.group(1)
    names = cls.split()
    if 'card' in names or 'card-raised' in names or any(x.startswith('btn-') for x in names):
        return None
    if not all(n in names for n in CARD_NEEDS) or not re.search(r'\brounded-(2xl|3xl)\b', cls):
        return None
    if '@' in cls:
        skipped['class built with a Razor expression'] += 1
        return None
    if not ('border-slate-200' in names or 'border-slate-200/80' in names) or 'dark:border-dgb-800' not in names:
        skipped['a border colour of its own'] += 1
        return None
    interactive = tag in ('a', 'button')
    kept, bad, had_hover = [], [], False
    for x in names:
        if CARD_DROP.match(x):
            continue
        if HOVER.match(x):
            had_hover = True
            continue
        if CARD_KEEP.match(x):
            kept.append(x)
        else:
            bad.append(x)
    if bad:
        skipped['unrecognised classes'] += 1
        unknown.update(bad)
        return None
    out = ['card']
    if interactive:
        out.append('card-interactive')
        kept = [k for k in kept if k not in ('card-hover', 'press-effect')]
    stats['card-interactive' if interactive else 'card'] += 1
    return attrs[:cm.start(1)] + ' '.join(out + kept) + attrs[cm.end(1):]


files = [f for pat in ('Pages/**/*.razor', 'Components/**/*.razor', 'Layout/**/*.razor')
         for f in glob.glob(os.path.join(root, pat), recursive=True)]
for path in sorted(files):
    rel = os.path.relpath(path, root).replace('\\', '/')
    if rel.startswith('Components/Dev/') or rel.endswith('TestAnimations.razor'):
        continue  # the gallery shows the old styles on purpose
    src = open(path, encoding='utf-8').read()
    before = sum(stats.values())

    if what == 'inputs':
        def sub(m):
            new = convert_field(m.group(1), m.group(2))
            return m.group(0) if new is None else '<' + m.group(1) + new + m.group(3) + '>'
        out = FIELD_TAG.sub(sub, src)
    else:
        def sub(m):
            new = convert_card(m.group(1), m.group(2))
            return m.group(0) if new is None else '<' + m.group(1) + new + '>'
        out = CARD_TAG.sub(sub, src)

    if out != src:
        changed.append((rel, sum(stats.values()) - before))
        if apply:
            open(path, 'w', encoding='utf-8', newline='').write(out)

print('converted:', dict(stats), 'in', len(changed), 'files')
for rel, n in changed:
    print('  %3d  %s' % (n, rel))
print('skipped:')
for r, n in skipped.most_common():
    print('  %3d  %s' % (n, r))
print('unrecognised class names:', unknown.most_common(40))
