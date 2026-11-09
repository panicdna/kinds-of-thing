"""Checks the YouTube videos shared in community/*.json before they are merged.

For every video id it asks YouTube's oEmbed endpoint; HTTP 200 means the video is public and may be
embedded, anything else (401/403/404...) means it cannot be played inside the app. The same rule is applied
by the app itself when it downloads a shared item.

Usage: python3 community/verify_videos.py [file.json ...]   (default: every community/*.json)
Exit status 1 when any video is malformed or cannot be played.
"""
import glob
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

VIDEO_ID = re.compile(r'[A-Za-z0-9_-]{11}')
MAX_VIDEOS = 5


def status_of(video_id, attempts=3):
    url = ('https://www.youtube.com/oembed?format=json&url='
           + urllib.parse.quote('https://www.youtube.com/watch?v=' + video_id, safe=''))
    request = urllib.request.Request(url, headers={'User-Agent': 'teum-community-check'})
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(request, timeout=20) as response:
                return response.status
        except urllib.error.HTTPError as error:
            return error.code  # a definite answer, e.g. 401 = embedding disabled, 404 = gone
        except (urllib.error.URLError, TimeoutError):
            time.sleep(2 * (attempt + 1))  # network hiccup: try again
    return None


def main(paths):
    problems = []
    checked = 0
    for path in paths:
        if path.replace('\\', '/').endswith('/index.json') or path == 'index.json':
            continue
        with open(path, encoding='utf-8') as handle:
            item = json.load(handle)
        videos = item.get('videos') or []
        if len(videos) > MAX_VIDEOS:
            problems.append(f'{path}: more than {MAX_VIDEOS} videos')
        seen = set()
        for video in videos:
            video_id = video.get('id', '') if isinstance(video, dict) else ''
            if not VIDEO_ID.fullmatch(video_id):
                problems.append(f'{path}: "{video_id}" is not an 11-character YouTube id')
                continue
            if video_id in seen:
                problems.append(f'{path}: duplicate video {video_id}')
                continue
            seen.add(video_id)
            checked += 1
            status = status_of(video_id)
            if status == 200:
                print(f'ok    {path}: {video_id}')
            elif status is None:
                problems.append(f'{path}: could not reach YouTube to check {video_id} (try again)')
            else:
                problems.append(f'{path}: {video_id} cannot be played in the app (YouTube answered {status})')
    print(f'{checked} video(s) checked')
    for problem in problems:
        print('FAIL  ' + problem)
    return 1 if problems else 0


if __name__ == '__main__':
    files = sys.argv[1:] or sorted(glob.glob('community/*.json'))
    sys.exit(main(files))
