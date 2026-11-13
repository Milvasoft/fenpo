"""Registers a downloaded track for a video in tracks.json, with its tempo analysis.

    python settrack.py <video-id> <file in tracks/> <pixabay page url> "<title>" "<author>" <author url> <profile comma list> [sfx_key] [minor]
"""

import json
import sys
from pathlib import Path

import beats

HERE = Path(__file__).resolve().parent
vid, file, url, title, author, author_url, profile = sys.argv[1:8]
sfx_key = int(sys.argv[8]) if len(sys.argv) > 8 else 0
minor = len(sys.argv) > 9 and sys.argv[9] == 'minor'
p = HERE / 'tracks.json'
data = json.load(open(p, encoding='utf-8')) if p.exists() else {}
old = data.get(vid, {})
an = old['analysis'] if old.get('file') == file else beats.analyse(str(HERE / 'tracks' / file))
data[vid] = {
    'file': file, 'title': title, 'author': author, 'author_url': author_url, 'source': url,
    'license': 'Pixabay Content License (https://pixabay.com/service/license-summary/)', 'content_id': False,
    'video_bpm': round(an['bpm'], 2), 'profile': [float(v) for v in profile.split(',')],
    'sfx_key': sfx_key, 'sfx_minor': minor, 'sfx_gain': old.get('sfx_gain', .45), 'drop': old.get('drop'), 'analysis': an,
}
json.dump(data, open(p, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(vid, an['bpm'], 'bars', len(an['bars']))
