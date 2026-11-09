#!/usr/bin/env python3
"""Build the offline poetry library from attributed Wikisource source fragments.

Python 3.10+; dependency: beautifulsoup4. Default rebuild uses only the shipped
poetry-sources.json. --refresh retrieves primary texts over verified HTTPS.
--check verifies the deterministic generated JS without changing files.
No model-authored verse, translations, sentence wrapping, or runtime network.
"""
from __future__ import annotations

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from datetime import date
import hashlib
import json
import os
from pathlib import Path
import re
import ssl
import time
import unicodedata
from urllib.parse import quote, unquote, urljoin, urlencode
from urllib.request import Request, urlopen

from bs4 import BeautifulSoup

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / 'poetry-sources.json'
OUTPUT = ROOT / 'poetry-data.js'
CACHE = Path(os.environ.get('TMPDIR', Path.home() / '.cache')) / 'desk-stretch-poetry'
RETRIEVED: set[str] = set()
UA = 'Mozilla/5.0 (compatible; DeskStretchPoetry/1.0; public-domain educational corpus)'
SEP = '\u241e'


def digest(value: str) -> str:
    return hashlib.sha256(value.encode('utf-8')).hexdigest()


def json_text(value) -> str:
    return json.dumps(value, ensure_ascii=False, indent=2)


def get(url: str) -> str:
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / (digest(url) + '.html')
    # A new --refresh run must not silently reuse yesterday's cached page.
    # Reuse only URLs that this process has already retrieved successfully.
    if url in RETRIEVED and path.exists():
        return path.read_text(encoding='utf-8')
    context = ssl.create_default_context()
    # Accept an explicitly configured trust bundle, including local enterprise CAs.
    if os.environ.get('SSL_CERT_FILE'):
        context.load_verify_locations(os.environ['SSL_CERT_FILE'])
    # Python 3.13+ strict AKI rules reject some enterprise CAs; keep certificate
    # chain + hostname verification enabled. No verify=False or unverified TLS.
    if os.environ.get('POETRY_COMPAT_CA') == '1':
        context.verify_flags &= ~ssl.VERIFY_X509_STRICT
    encoded = quote(url, safe=':/?&=()%+')
    for attempt in range(3):
        try:
            with urlopen(Request(encoded, headers={'User-Agent': UA}), context=context, timeout=60) as response:
                body = response.read().decode('utf-8')
            if 'wgRevisionId' not in body and 'mw:revision' not in body and '"query"' not in body:
                if 'wikisource.org' in url:
                    raise ValueError(f'Not a Wikisource content/API response: {url}')
            path.write_text(body, encoding='utf-8')
            RETRIEVED.add(url)
            return body
        except Exception:
            if attempt == 2:
                raise
            time.sleep(1 + attempt * 2)
    raise AssertionError('unreachable')


def revision(html: str) -> int:
    for pattern in (r'"wgRevisionId":(\d+)', r'property="mw:revisionId" content="(\d+)"', r'revision id (\d+)'):
        found = re.search(pattern, html)
        if found:
            return int(found.group(1))
    raise ValueError('Wikisource revision ID not found')


def cleaned(fragment: str):
    soup = BeautifulSoup(fragment, 'html.parser')
    for node in soup.select('.pagenum, .ws-noexport, .noprint, .mw-editsection, sup.reference, style, script, center, big'):
        node.decompose()
    return soup


def extract_units(fragment: str, unit_type: str) -> list[str]:
    soup = cleaned(fragment)
    if soup.select('.ws-poem-line'):
        units = []
        for stanza in soup.select('.ws-poem-stanza'):
            if units:
                units.append('')
            units.extend(re.sub(r'\s+', ' ', line.get_text()).strip()
                         for line in stanza.select('.ws-poem-line'))
        return units
    if unit_type == 'source-verse-paragraph' and not any(
            p.find('br') and p.get_text(strip=True) for p in soup.select('p')):
        # Some pages have one p per verse line; others have a p per stanza
        # with explicit br verse breaks. Never collapse those internal brs.
        return [re.sub(r'\s+', ' ', p.get_text()).strip() for p in soup.select('p')]
    if unit_type == 'original-paragraph':
        # Tagore is prose poetry. Use original paragraphs, NEVER wrap sentences.
        return [text for p in soup.select('p')
                if (text := re.sub(r'\s+', ' ', p.get_text().replace('\u200b', '')).strip())
                and not text.isdigit()]
    for br in soup.find_all('br'):
        br.replace_with(SEP)
    for p in soup.find_all('p'):
        p.append(SEP + SEP)
    units = [re.sub(r'\s+', ' ', part.replace('\u200b', '')).strip()
             for part in soup.get_text().split(SEP)]
    while units and not units[0]:
        units.pop(0)
    while units and not units[-1]:
        units.pop()
    # Empty source paragraphs can make multiple stanza gaps; a single gap suffices.
    return [line for i, line in enumerate(units) if line or (i and units[i - 1])]


def page_url(lang: str, title: str) -> str:
    return f'https://{lang}.wikisource.org/wiki/' + quote(title.replace(' ', '_'), safe='/():,!')


def collect_one(spec: dict) -> dict:
    lang, title = spec['language'], spec['sourceTitle']
    html = get(page_url(lang, title))
    soup = BeautifulSoup(html, 'html.parser')
    original = False
    if spec['category'] == 'nobel':
        node = soup.select_one('.prp-pages-output')
        unit_type = 'original-paragraph'
        original = True
    else:
        candidates = soup.select('.poem, .ws-poem')
        unit_type = 'original-verse-line'
        if not candidates and spec['author'] == '김소월':
            # A few source pages mark each verse line as its own HTML paragraph.
            # Keep only direct body paragraphs before the copyright heading.
            parent = soup.select_one('.mw-parser-output')
            node = soup.new_tag('div')
            for child in list(parent.children):
                if getattr(child, 'name', None) == 'div' and 'mw-heading' in child.get('class', []):
                    break
                if getattr(child, 'name', None) == 'p':
                    node.append(child.extract())
            if not node.select('p'):
                raise ValueError(f'No source verse paragraphs: {title}')
            unit_type = 'source-verse-paragraph'
        elif not candidates:
            raise ValueError(f'No verse markup: {title}')
        elif spec['author'] == '한용운':
            node = candidates[0]  # scanned 1926 original, NOT following 현대어 section
            original = True
        elif spec['author'] == '김소월':
            # Most two-version pages label the second block 원문. A one-block
            # page is a transcription; keep its editorial attribution/license.
            node = candidates[-1]
            original = len(candidates) > 1
        else:
            node = candidates[0]
            original = True
    if node is None:
        raise ValueError(f'Missing source body: {title}')
    fragment = str(node)
    units = extract_units(fragment, unit_type)
    if len([line for line in units if line]) < 2:
        raise ValueError(f'Insufficient original line/paragraph units: {title}')
    edition = spec['edition']
    if spec['author'] == '김소월':
        edition += ' · 위키문헌 원문 표기' if original else ' · 위키문헌 전사 표기(편집 기여 CC BY-SA 4.0)'
    rev = revision(html)
    source = f'https://{lang}.wikisource.org/w/index.php?' + urlencode({'title': title.replace('_', ' '), 'oldid': rev})
    transclusions = sorted(set(el.get('data-page-name') for el in soup.select('[data-page-name]')))
    return {**spec, 'edition': edition, 'source': source, 'revisionId': rev,
            'sourcePage': page_url(lang, title), 'unitType': unit_type,
            'transcription': 'original-edition-spelling' if original else 'Wikisource-transcription-spelling',
            'rawFragment': fragment, 'fragmentSha256': digest(fragment),
            'renderSha256': digest(html), 'linesSha256': digest(json_text(units)),
            'editorialLicenseNotice': footer_notice(soup),
            'historyUrl': f'https://{lang}.wikisource.org/w/index.php?' + urlencode({'title': title.replace('_', ' '), 'action': 'history'}),
            'transcludedPages': transclusions}


def footer_notice(soup) -> dict:
    node = soup.select_one('#footer-info-copyright')
    if node is None:
        raise ValueError('Source has no editorial license footer')
    return {'text': node.get_text(' ', strip=True),
            'links': [a['href'] for a in node.select('a[href]')]}


def collect_evidence() -> list[dict]:
    result = []
    authors = [('김소월', 'ko', '저자:김소월', 1934),
               ('한용운', 'ko', '저자:한용운', 1944),
               ('Rabindranath Tagore', 'en', 'Author:Rabindranath Tagore', 1941),
               ('Jane Taylor', 'en', 'Author:Jane Taylor', 1824),
               ('John Newton', 'en', 'Author:John Newton (1725-1807)', 1807),
               ('John Howard Payne', 'en', 'Author:John Howard Payne', 1852)]
    for author, lang, title, death in authors:
        html = get(page_url(lang, title))
        soup = BeautifulSoup(html, 'html.parser')
        notices = soup.select('.licenseContainer')
        designation = ' '.join(node.get_text(' ', strip=True) for node in notices)
        if not re.search(r'퍼블릭 도메인|public domain', designation, re.I):
            raise ValueError(f'No public-domain designation for {author}')
        # Death year must occur in the retrieved author biography, not just specs.
        if str(death) not in soup.get_text():
            raise ValueError(f'Unverified author death year: {author}')
        result.append({'author': author, 'authorDeathYear': death,
                       'source': page_url(lang, title), 'revisionId': revision(html),
                       'publicDomainDesignation': designation, 'renderSha256': digest(html),
                       'editionRule': 'Only the specified pre-1931 editions are included; later translations/editions are not implied to be free.'})
    title = 'Gitanjali'
    html = get(page_url('en', title))
    soup = BeautifulSoup(html, 'html.parser')
    designation = ' '.join(n.get_text(' ', strip=True) for n in soup.select('.licenseContainer'))
    if not designation or '1913' not in html:
        raise ValueError('Gitanjali edition/public-domain designation missing')
    result.append({'collection': 'Gitanjali (1913)', 'source': page_url('en', title),
                   'revisionId': revision(html), 'publicDomainDesignation': designation,
                   'renderSha256': digest(html)})
    url = 'https://www.nobelprize.org/prizes/literature/1913/tagore/facts/'
    html = get(url)
    soup = BeautifulSoup(html, 'html.parser')
    text = soup.get_text(' ', strip=True)
    pos = text.find('Rabindranath Tagore Nobel Prize in Literature 1913')
    if pos < 0:
        raise ValueError('Official Tagore Nobel evidence not found')
    result.append({'author': 'Rabindranath Tagore', 'nobelYear': 1913, 'source': url,
                   'evidence': text[pos:pos + 100], 'renderSha256': digest(html)})
    return result


def resolve_transclusions(records: list[dict]) -> None:
    # The public API can rate-limit long multi-title queries. Source page HTML
    # also exposes its revision ID, and avoids query normalization ambiguity.
    for lang in ('ko', 'en'):
        titles = sorted({title for record in records if record['language'] == lang
                         for title in record['transcludedPages']})
        resolved = {}
        def resolve(title):
            html = get(page_url(lang, title))
            rev = revision(html)
            return {'title': title.replace('_', ' '), 'revisionId': rev,
                    'source': f'https://{lang}.wikisource.org/w/index.php?' + urlencode({'title': title.replace('_', ' '), 'oldid': rev})}
        with ThreadPoolExecutor(max_workers=2) as pool:
            for entry in pool.map(resolve, titles):
                resolved[entry['title']] = entry
        for record in records:
            if record['language'] == lang:
                record['transcludedPages'] = [resolved[title.replace('_', ' ')] for title in record['transcludedPages']]


def specs() -> list[dict]:
    html = get(page_url('ko', '진달래꽃 (시집)'))
    soup = BeautifulSoup(html, 'html.parser')
    paths = {}
    for a in soup.select('.mw-parser-output a[href]'):
        path = unquote(a['href'])
        if path.startswith('/wiki/진달래꽃_(시집)/'):
            paths[path.removeprefix('/wiki/')] = a.get_text(strip=True)
    if len(paths) < 100:
        raise ValueError(f'Expected at least 100 Sowol TOC entries, found {len(paths)}')
    result = [{'id': f'sowol-{i:03d}', 'title': title, 'sourceTitle': path,
               'author': '김소월', 'year': 1925, 'language': 'ko', 'category': 'lyric',
               'edition': '진달래꽃 (1925); 수록 판본 연도'}
              for i, (path, title) in enumerate(paths.items(), 1)]
    han = ['님의 침묵', '이별은 미의 창조', '알 수 없어요', '나는 잊고저', '가지 마셔요',
           '고적한 밤', '나의 길', '꿈 깨고서', '예술가', '나룻배와 행인']
    result += [{'id': f'han-{i:03d}', 'title': title, 'sourceTitle': '님의 침묵/' + title,
                'author': '한용운', 'year': 1926, 'language': 'ko', 'category': 'lyric',
                'edition': '님의 沈默 (1926), 회동서관; 원문 표기'}
               for i, title in enumerate(han, 1)]
    result += [{'id': f'tagore-gitanjali-{i:03d}', 'title': f'Gitanjali {i} (English prose poem)',
                'sourceTitle': f'Gitanjali/{i}', 'author': 'Rabindranath Tagore', 'year': 1913,
                'language': 'en', 'category': 'nobel', 'nobelYear': 1913,
                'edition': 'Gitanjali (Song Offerings), Macmillan 1913; author’s English rendering; original prose-poem paragraphs, not verse lines'}
               for i in range(1, 11)]
    for ident, title, path, author, year, edition in [
        ('song-star', 'The Star (Twinkle, Twinkle, Little Star)', 'Rhymes for the Nursery/The Star', 'Jane Taylor', 1806, 'Rhymes for the Nursery (1806); English song lyrics'),
        ('song-amazing-grace', 'Amazing Grace (Faith’s Review and Expectation)', 'Olney Hymns (1840)/Book 1/Hymn 41', 'John Newton', 1840, 'Olney Hymns (1840), Book I, Hymn 41; original six stanzas; first published 1779'),
        ('song-home-sweet-home', 'Home, Sweet Home!', 'Poems That Every Child Should Know/Home, Sweet Home', 'John Howard Payne', 1904, 'Poems That Every Child Should Know (1904), Mary E. Burt (editor); English song lyrics, originally 1823'),
    ]:
        result.append({'id': ident, 'title': title, 'sourceTitle': path, 'author': author,
                       'year': year, 'language': 'en', 'category': 'song', 'edition': edition})
    return result


def poem(record: dict) -> dict:
    result = {key: record[key] for key in ('id', 'title', 'author', 'year', 'language', 'category')}
    result.update(lines=extract_units(record['rawFragment'], record['unitType']),
                  source=record['source'], rights='public-domain', edition=record['edition'])
    if 'nobelYear' in record:
        result['nobelYear'] = record['nobelYear']
    if digest(json_text(result['lines'])) != record['linesSha256']:
        raise ValueError(f'Line extraction drift: {record["id"]}')
    if digest(record['rawFragment']) != record['fragmentSha256']:
        raise ValueError(f'Source fragment changed: {record["id"]}')
    return result


def validate(poems: list[dict]) -> None:
    seen = set()
    for p in poems:
        body = re.sub(r'[^\w]', '', unicodedata.normalize('NFKC', ''.join(p['lines'])))
        if body in seen:
            raise ValueError(f'Duplicate poem: {p["id"]}')
        seen.add(body)
    assert len(poems) >= 110
    assert sum(p['language'] == 'ko' for p in poems) > len(poems) / 2
    assert sum(p['category'] == 'nobel' for p in poems) >= 10
    assert sum(p['category'] == 'song' for p in poems) >= 3
    assert len({p['author'] for p in poems}) >= 3


def refresh() -> dict:
    records = []
    with ThreadPoolExecutor(max_workers=4) as pool:
        for record in pool.map(collect_one, specs()):
            records.append(record)
            print(f'{len(records):3d}: {record["title"]}', flush=True)
    resolve_transclusions(records)
    evidence = collect_evidence()
    return {'schema': 1, 'checkedAt': date.today().isoformat(),
            'copyrightEvidence': evidence,
            'copyrightScope': 'Public-domain underlying texts only; pre-1931 editions and authors deceased more than 70 years. Not a claim that contemporary lyrics, translations, performances, or arrangements are public domain.',
            'attribution': 'Wikisource contributors; each entry preserves a source title, revision/history link, and verbatim source HTML fragment. Editorial transcription/modernized orthography contributions, if copyrightable, are retained under CC BY-SA 4.0; the original underlying works remain public domain. No contemporary third-party translations included.',
            'editorialLicense': {'name': 'Creative Commons Attribution-ShareAlike 4.0 International',
                                 'url': 'https://creativecommons.org/licenses/by-sa/4.0/',
                                 'terms': 'https://creativecommons.org/licenses/by-sa/4.0/legalcode.en',
                                 'modifications': 'HTML/navigation/title/page-number markup removed; whitespace collapsed within original verse lines; stanza gaps retained. Tagore uses original prose paragraphs. No generated lines or translations.'},
            'records': records}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--refresh', action='store_true', help='Retrieve live primary texts; requires network')
    parser.add_argument('--check', action='store_true', help='Offline reproducibility/integrity check, no writes')
    args = parser.parse_args()
    if args.refresh and args.check:
        parser.error('--refresh and --check are mutually exclusive')
    manifest = refresh() if args.refresh else json.loads(MANIFEST.read_text(encoding='utf-8'))
    poems = [poem(record) for record in manifest['records']]
    validate(poems)
    payload = {'poems': poems, 'checkedAt': manifest['checkedAt']}
    generated = ('/* Offline public-domain underlying works. Editorial attribution/license and\n'
                 ' * original source units: poetry-sources.json. Generated by tools/build_poetry.py.\n'
                 ' * English Tagore items are prose poems: each array unit is an original paragraph. */\n'
                 'window.POEM_LIBRARY = ' + json_text(payload) + ';\n')
    if args.check:
        if OUTPUT.read_text(encoding='utf-8') != generated:
            raise SystemExit('poetry-data.js is stale; run python tools/build_poetry.py')
        print('PASS: offline rebuild exactly matches poetry-data.js and all source hashes')
    else:
        if args.refresh:
            MANIFEST.write_text(json_text(manifest) + '\n', encoding='utf-8')
        OUTPUT.write_text(generated, encoding='utf-8')
    print(json_text({'total': len(poems), 'byAuthor': dict(Counter(p['author'] for p in poems)),
                     'byCategory': dict(Counter(p['category'] for p in poems)),
                     'byLanguage': dict(Counter(p['language'] for p in poems))}))


if __name__ == '__main__':
    main()
