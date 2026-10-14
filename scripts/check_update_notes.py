"""Validate the bilingual plain-text Workshop style approved from beta4."""
import argparse
import re
from pathlib import Path


def validate_update_notes(text, version):
    lines = text.replace('\r\n', '\n').strip().split('\n')
    if any(re.search(r'\[/?(?:h\d|b|i|list|olist|\*|url|img)(?:[=\]])|^\s*#', line, re.I) for line in lines):
        raise ValueError('Use beta4 plain-text headings and numbered/hyphen items, without BBCode/Markdown headings.')
    if lines.count('English') != 1:
        raise ValueError('Separate the English section with one standalone English line.')
    separator = lines.index('English')
    if separator == 0 or separator == len(lines)-1 or lines[separator-1] or lines[separator+1]:
        raise ValueError('Leave a blank line before and after English.')
    groups = [lines[:separator], lines[separator+1:]]
    shapes = []
    for index, group in enumerate(groups):
        while group and not group[0]: group = group[1:]
        while group and not group[-1]: group = group[:-1]
        if len(group) < 4 or not re.match(r'^V' + re.escape(version) + r'(?:\s|[（(·])', group[0]) or group[1]:
            raise ValueError('Both languages must begin with the current version and a blank line.')
        sections = []
        current = None
        for offset, line in enumerate(group[2:], start=2):
            if not line: continue
            item = re.match(r'^(\d+)\. (\S.*)$', line)
            if line.startswith('- ') or item:
                if current is None: raise ValueError('Items must follow a category heading.')
                kind = 'number' if item else 'hyphen'
                if current['kind'] not in (None, kind): raise ValueError('Keep one item style per category.')
                current['kind'] = kind
                if item and int(item[1]) != current['count']+1:
                    raise ValueError('Restart each numbered category at 1 and keep consecutive numbering.')
                if len(line) > 360 or not line[2:].strip(): raise ValueError('Keep each item on one readable line.')
                current['count'] += 1
            else:
                colon = '：' if index == 0 else ':'
                if not line.endswith(colon) or len(line) > 80 or group[offset-1]:
                    raise ValueError('Each category needs a short colon heading, separated by a blank line.')
                if current and not current['count']: raise ValueError('Do not leave an empty category.')
                current = {'kind': None, 'count': 0}
                sections.append(current)
        if not sections or not current['count']: raise ValueError('Each language needs populated categories.')
        shapes.append(sections)
    if shapes[0] != shapes[1]: raise ValueError('Chinese and English category/item counts must match.')
    return {'style': 'v1.2.0-beta4-plain-text', 'version': version, 'sections_per_language': len(shapes[0]),
            'items_per_language': sum(section['count'] for section in shapes[0])}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('path', type=Path)
    parser.add_argument('--version', required=True)
    args = parser.parse_args()
    print(validate_update_notes(args.path.read_text(encoding='utf-8-sig'), args.version))
