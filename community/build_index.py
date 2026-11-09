"""Writes community/index.json: the file names and git blob shas the app syncs.

The app reads this one file from raw.githubusercontent.com, which has no API
quota, and downloads only the items whose sha changed. Run from the repository
root after `git add`; the GitHub Action does the same after every merge.
"""
import json
import re
import subprocess

NAME = re.compile(r'[a-z0-9][a-z0-9-]{2,39}\.json')
staged = subprocess.check_output(['git', 'ls-files', '-s', '--', 'community'], text=True)
items = []
for line in staged.splitlines():
    meta, path = line.split('\t', 1)
    name = path.split('/', 1)[1]
    if name != 'index.json' and NAME.fullmatch(name):
        items.append({'name': name, 'sha': meta.split()[1]})
items.sort(key=lambda item: item['name'])
with open('community/index.json', 'w', encoding='utf-8', newline='\n') as handle:
    json.dump({'schema': 1, 'items': items}, handle, ensure_ascii=False, indent=2)
    handle.write('\n')
print(f'{len(items)} item(s) indexed')
