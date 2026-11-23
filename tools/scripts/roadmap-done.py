"""Move a finished ROADMAP.md row to ROADMAP-DONE.md under the same section.
Usage: python tools/scripts/roadmap-done.py "<row start text>" "<status, e.g. done (b/x merged, GV 83)>"
"""
import re, sys
prefix, status = sys.argv[1], sys.argv[2]
rm = open('ROADMAP.md', encoding='utf-8', newline='').read().split('\n')
dn = open('ROADMAP-DONE.md', encoding='utf-8', newline='').read().rstrip('\n').split('\n')
sec, hit = None, None
for i, l in enumerate(rm):
    if l.startswith('## '): sec = l
    if l.startswith('| ' + prefix): hit = i; break
if hit is None: sys.exit('row not found: ' + prefix)
row = re.sub(r'\|[^|]*\|\s*$', '| ' + status + ' |', rm.pop(hit))
if sec in dn:
    j = dn.index(sec) + 1
    while j < len(dn) and not dn[j].startswith('## '): j += 1
    while dn[j - 1] == '': j -= 1
    dn.insert(j, row)
else:
    dn += ['', sec, '', '| Item | Status |', '|---|---|', row]
open('ROADMAP.md', 'w', encoding='utf-8', newline='').write('\n'.join(rm))
open('ROADMAP-DONE.md', 'w', encoding='utf-8', newline='').write('\n'.join(dn) + '\n')
print('moved to', sec)
