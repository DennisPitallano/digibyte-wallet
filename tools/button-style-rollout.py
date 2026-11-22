"""Moves hand-written button classes to the shared btn-* styles (docs/UI_STYLE.md).

Usage: python btn_rollout.py <web project dir> [--apply]
Without --apply it only reports what it would do.
"""
import collections
import glob
import os
import re
import sys

root = sys.argv[1]
apply = '--apply' in sys.argv

# An opening <button ...> or <a ...> tag, across lines. Razor expressions with quotes inside attributes are handled by
# only touching a plain class="..." attribute (no @ inside).
TAG = re.compile(r'<(button|a)\b([^<>]*?(?:"[^"]*"[^<>]*?)*)>', re.S)
CLASS = re.compile(r'\bclass="([^"@]*)"')

KEEP = re.compile(r'^(w-full|flex-1|flex-none|flex-shrink-0|shrink-0|grow|self-\w+|col-span-\d+|order-\d+|'
                  r'-?m[trblxy]?-[\w.\[\]]+|sm:w-auto|truncate|whitespace-nowrap|no-underline|relative|'
                  r'slide-up(-delay-\d)?|fade-in|scale-in|max-w-\S+|min-w-\S+|ml-auto|mr-auto|mx-auto|hidden|sm:\S+|group|z-\d+)$')
DROP = re.compile(r'^(bg-\S+|hover:\S+|dark:\S+|active:\S+|focus:\S+|focus-visible:\S+|disabled:\S+|group-\S+|'
                  r'text-(white|sm|base|xs|\[\d+px\]|slate-\d+|dgb-\d+|red-\d+|center)|font-(semibold|medium|bold)|'
                  r'rounded(-\w+)?|shadow(-\S+)?|transition(-\w+)?|duration-\d+|ease-\S+|press-effect|'
                  r'p[xy]?-[\d.]+|border(-\S+)?|min-h-\[\d+px\]|flex|inline-flex|items-center|justify-center|gap-[\d.]+|'
                  r'inline-block|cursor-pointer|select-none|leading-\S+|tracking-\S+|block|text-center|outline-none|card-hover)$')
FIXED_SIZE = re.compile(r'^(w|h)-(\d+|\[\d+px\])$')
SMALL = re.compile(r'^(text-xs|text-\[1[0-2]px\]|py-(1|1\.5|2|2\.5)|min-h-\[4[04]px\])$')

stats = collections.Counter()
skipped = collections.defaultdict(list)
changed_files = []


def kind_of(tag, classes):
    c = set(classes)
    if any(FIXED_SIZE.match(x) for x in classes):
        return None, 'fixed size (icon button)'
    if 'rounded-full' in c:
        return None, 'pill or chip'
    if {'text-left', 'flex-col', 'justify-between', 'items-start'} & c:
        return None, 'row or card, not a button'
    if ('flex' in c or 'inline-flex' in c) and 'justify-center' not in c:
        return None, 'content not centred (a row, not a button)'
    if not any(re.match(r'^py-[\d.]+$', x) for x in classes):
        return None, 'no vertical padding (not a standard button)'
    if 'text-white' in c and 'bg-dgb-500' in c:
        return 'btn-primary', None
    if 'text-white' in c and ({'bg-red-500', 'bg-red-600'} & c):
        return 'btn-danger', None
    if tag == 'button' and ({'font-semibold', 'font-medium'} & c):
        if 'bg-slate-100' in c and 'dark:bg-dgb-800' in c:
            return 'btn-secondary', None
        if 'bg-white' in c and 'dark:bg-dgb-900' in c and 'border' in c:
            return 'btn-secondary', None
    return None, None


def convert(tag, value):
    classes = value.split()
    if any(x.startswith('btn-') for x in classes):
        return None
    kind, why = kind_of(tag, classes)
    if kind is None:
        if why and ('bg-dgb-500' in classes or 'bg-red-500' in classes):
            return ('skip', why)
        return None
    kept, unknown = [], []
    for x in classes:
        if KEEP.match(x):
            kept.append(x)
        elif DROP.match(x):
            continue
        else:
            unknown.append(x)
    if unknown:
        return ('skip', 'unrecognised classes: ' + ' '.join(unknown))
    out = [kind]
    if any(SMALL.match(x) for x in classes):
        out.append('btn-sm')
    return ('ok', ' '.join(out + kept), kind)


files = [f for pat in ('Pages/**/*.razor', 'Components/**/*.razor', 'Layout/**/*.razor')
         for f in glob.glob(os.path.join(root, pat), recursive=True)]
for path in sorted(files):
    rel = os.path.relpath(path, root).replace('\\', '/')
    if rel.startswith('Components/Dev/') or rel.endswith('TestAnimations.razor'):
        continue  # the gallery shows the old styles on purpose
    src = open(path, encoding='utf-8').read()
    count = 0

    def tag_sub(m):
        global count
        tag, attrs = m.group(1), m.group(2)
        cm = CLASS.search(attrs)
        if not cm:
            if 'class="' in attrs and ('bg-dgb-500' in attrs or 'bg-red-500' in attrs):
                skipped[rel].append('class built with a Razor expression')
            return m.group(0)
        res = convert(tag, cm.group(1))
        if res is None:
            return m.group(0)
        if res[0] == 'skip':
            skipped[rel].append(res[1])
            return m.group(0)
        stats[res[2]] += 1
        count += 1
        new_attrs = attrs[:cm.start(1)] + res[1] + attrs[cm.end(1):]
        return '<' + tag + new_attrs + '>'

    out = TAG.sub(tag_sub, src)
    if out != src:
        changed_files.append((rel, count))
        if apply:
            open(path, 'w', encoding='utf-8', newline='').write(out)

print('converted:', dict(stats), 'in', len(changed_files), 'files')
for rel, n in changed_files:
    print('  %3d  %s' % (n, rel))
print('skipped:')
reasons = collections.Counter(r if not r.startswith('unrecognised') else 'unrecognised classes' for v in skipped.values() for r in v)
for r, n in reasons.most_common():
    print('  %3d  %s' % (n, r))
unk = collections.Counter(x for v in skipped.values() for r in v if r.startswith('unrecognised') for x in r.split(': ')[1].split())
print('unrecognised class names:', unk.most_common(40))
