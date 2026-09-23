#!/usr/bin/env python3
"""Checks and builds the GitHub Wiki pages kept in wiki/.

wiki/ is the source of the repository's GitHub Wiki. Pages link to each other
by file name (`Page-Name.md#heading`) and embed images from wiki/images/, so
the folder reads correctly on GitHub as it is. `build` rewrites those links for
the wiki, whose page URLs have no .md suffix and which does not serve the
images: page links become wiki URLs, and images point at the published commit.

  python3 scripts/wiki.py check
  python3 scripts/wiki.py build OUT_DIR --repo OWNER/REPO --ref COMMIT

Only the standard library is used, so the check runs anywhere Python 3 does.
"""
import argparse
import os
import pathlib
import re
import sys

WIKI = pathlib.Path(__file__).resolve().parent.parent / 'wiki'
SPECIAL = ('_Sidebar', '_Footer')
PAGE_FILE = re.compile(r'^(?:[A-Z][A-Za-z0-9-]*|_Sidebar|_Footer)\.md$')

FENCE = re.compile(r'^\s*(`{3,}|~{3,})')
HEADING = re.compile(r'^\s{0,3}(#{1,6})\s+(.*?)(?:\s+#+)?\s*$')
CODE_SPAN = re.compile(r'(`+)(.+?)\1')
# [text](target) and ![alt](target); the text may hold one level of brackets.
LINK = re.compile(r'(!?)\[((?:[^\[\]]|\[[^\]]*\])*)\]\(\s*<?([^)\s>]*)>?(?:\s+"[^"]*")?\s*\)')
IMG_SRC = re.compile(r'(<img\b[^>]*?\bsrc=")([^"]+)(")', re.I)
PAGE_LINK = re.compile(r'^([A-Z][A-Za-z0-9-]*)\.md(?:#(.*))?$')
SCHEME = re.compile(r'^[a-z][a-z0-9+.-]*:', re.I)

# Syntax that renders on other Markdown sites but not on GitHub, or that the
# wiki handles differently. Checked outside code blocks and code spans.
LEFTOVERS = [
    (re.compile(r'^#\s'), 'a "#" title: GitHub shows the page name as the title, so start at "##"'),
    (re.compile(r'\+\+[a-z0-9]+(?:\+[a-z0-9]+)*\+\+', re.I), 'MkDocs key syntax: use <kbd>Ctrl</kbd>+<kbd>C</kbd>'),
    (re.compile(r'\)\{[^}\n]*\}|\{:?\s*[.#][A-Za-z][\w-]*[^}\n]*\}'), 'an attribute list: GitHub prints it as text'),
    (re.compile(r'^\s*=== "'), 'a content tab: GitHub does not render tabs'),
    (re.compile(r'^:\s{3}'), 'a definition list: GitHub does not render them'),
    (re.compile(r'^\s*(?:!!!|\?\?\?)\s'), 'an MkDocs admonition: use a "> **Note:**" blockquote'),
    (re.compile(r'^\s*>\s*\[!(?:NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]', re.I), 'an alert: use a "> **Note:**" blockquote, which renders in the wiki too'),
    (re.compile(r':(?:material|octicons|fontawesome|simple)-[\w-]+:'), 'an MkDocs icon shortcode'),
    (re.compile(r'<div\b[^>]*\bmarkdown\b'), 'Markdown inside HTML: GitHub ignores the markdown attribute'),
    (re.compile(r'konradcinkusz\.github\.io/agent-helm/[^\s)>"]'), 'a link into the old documentation site: link to the wiki page instead'),
    (re.compile(r'github\.com/[^/\s)]+/[^/\s)]+/wiki/[^\s)>"]'), 'an absolute wiki link: link to the page file (Page.md) so the check can follow it'),
]


def lines_outside_code(text):
    """Yields (line number, raw line, line with code spans blanked) outside fenced code."""
    fence = None
    for number, line in enumerate(text.split('\n'), 1):
        match = FENCE.match(line)
        if fence:
            marker = line.strip()
            if match and set(marker) == {fence[0]} and len(marker) >= len(fence):
                fence = None
            continue
        if match:
            fence = match.group(1)
            continue
        yield number, line, CODE_SPAN.sub(lambda span: ' ' * len(span.group(0)), line)


def slug(heading):
    """GitHub's anchor for a heading: lower case, punctuation dropped, spaces to hyphens."""
    text = re.sub(r'!\[[^\]]*\]\([^)]*\)', '', heading)
    text = re.sub(r'\[([^\]]*)\]\([^)]*\)', r'\1', text)
    text = re.sub(r'<[^>]+>', '', text).replace('`', '')
    return re.sub(r'[^\w\- ]', '', text.strip().lower()).replace(' ', '-')


def anchors(text):
    seen, result = {}, set()
    for _, raw, _ in lines_outside_code(text):
        match = HEADING.match(raw)
        if not match:
            continue
        base = slug(match.group(2))
        count = seen.get(base, 0)
        seen[base] = count + 1
        result.add(base if count == 0 else f'{base}-{count}')
    return result


def load_pages():
    return {path.stem: path.read_text(encoding='utf-8') for path in sorted(WIKI.glob('*.md'))}


def check():
    errors = []

    def error(path, line, message):
        errors.append((path, line, message))

    pages = load_pages()
    page_anchors = {name: anchors(text) for name, text in pages.items()}
    used_images = set()
    in_sidebar = set()

    for path in sorted(WIKI.rglob('*.md')):
        if path.parent != WIKI:
            error(path, 0, 'pages must sit directly in wiki/: the wiki has no folders')
        elif not PAGE_FILE.match(path.name):
            error(path, 0, 'page file names start with a capital letter and use only letters, digits and hyphens')
    for name in ('Home', *SPECIAL):
        if name not in pages:
            error(WIKI / f'{name}.md', 0, 'missing')

    for name, text in pages.items():
        path = WIKI / f'{name}.md'
        if text.startswith('---\n'):
            error(path, 1, 'front matter: GitHub shows it as a table')
        for number, raw, line in lines_outside_code(text):
            for pattern, message in LEFTOVERS:
                if pattern.search(line):
                    error(path, number, message)
            targets = [(bool(bang), target) for bang, _, target in LINK.findall(line)]
            targets += [(True, src) for _, src, _ in IMG_SRC.findall(line)]
            for is_image, target in targets:
                if not target:
                    error(path, number, 'empty link target')
                elif SCHEME.match(target):
                    continue
                elif target.startswith('#'):
                    if target[1:] not in page_anchors[name]:
                        error(path, number, f'no heading for {target} on this page')
                elif target.startswith('images/'):
                    if not (WIKI / target).is_file():
                        error(path, number, f'missing image {target}')
                    used_images.add(target)
                elif match := PAGE_LINK.match(target):
                    page, anchor = match.groups()
                    if is_image:
                        error(path, number, f'{target} is a page, not an image')
                    elif page not in pages or page in SPECIAL:
                        error(path, number, f'no page {page}.md')
                    elif anchor is not None and anchor not in page_anchors[page]:
                        error(path, number, f'no heading for #{anchor} on {page}')
                    if name == '_Sidebar':
                        in_sidebar.add(page)
                else:
                    error(path, number, f'unsupported link {target}: link to a page file (Page.md), an image in images/, or a full URL')

    for name in pages:
        if name not in SPECIAL and name not in in_sidebar:
            error(WIKI / '_Sidebar.md', 0, f'{name}.md is not in the sidebar')
    for image in sorted(WIKI.glob('images/*')):
        if f'images/{image.name}' not in used_images:
            error(image, 0, 'not used by any page')

    github = os.environ.get('GITHUB_ACTIONS') == 'true'
    for path, line, message in errors:
        where = path.relative_to(WIKI.parent)
        if github:
            print(f'::error file={where},line={max(line, 1)}::{message}')
        else:
            print(f'{where}:{line}: {message}' if line else f'{where}: {message}')
    if errors:
        print(f'{len(errors)} problem(s) in wiki/', file=sys.stderr)
        return 1
    print(f'wiki/ is fine: {len(pages) - len(SPECIAL)} pages')
    return 0


def build(out, repo, ref):
    """Writes the pages as the GitHub Wiki needs them: wiki URLs and pinned images."""
    wiki_url = f'https://github.com/{repo}/wiki'
    image_url = f'https://raw.githubusercontent.com/{repo}/{ref}/wiki'

    def target(value):
        if match := PAGE_LINK.match(value):
            page, anchor = match.groups()
            return f'{wiki_url}/{page}' + (f'#{anchor}' if anchor is not None else '')
        if value.startswith('images/'):
            return f'{image_url}/{value}'
        return value

    def rewrite(text):
        text = LINK.sub(lambda m: m.group(0).replace(f']({m.group(3)}', f']({target(m.group(3))}', 1), text)
        return IMG_SRC.sub(lambda m: m.group(1) + target(m.group(2)) + m.group(3), text)

    out.mkdir(parents=True, exist_ok=True)
    for name, text in load_pages().items():
        result, fence = [], None
        for line in text.split('\n'):
            match = FENCE.match(line)
            if fence:
                marker = line.strip()
                if match and set(marker) == {fence[0]} and len(marker) >= len(fence):
                    fence = None
                result.append(line)
                continue
            if match:
                fence = match.group(1)
                result.append(line)
                continue
            # Rewrite everything but code spans.
            parts, last = [], 0
            for span in CODE_SPAN.finditer(line):
                parts.append(rewrite(line[last:span.start()]))
                parts.append(span.group(0))
                last = span.end()
            parts.append(rewrite(line[last:]))
            result.append(''.join(parts))
        (out / f'{name}.md').write_text('\n'.join(result), encoding='utf-8')
    print(f'built {len(load_pages())} files into {out}')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('check', help='check links, headings, images and syntax')
    build_parser = commands.add_parser('build', help='write the pages as the GitHub Wiki needs them')
    build_parser.add_argument('out', type=pathlib.Path)
    build_parser.add_argument('--repo', required=True, help='OWNER/REPO')
    build_parser.add_argument('--ref', required=True, help='commit the images are served from')
    args = parser.parse_args()
    if args.command == 'check':
        return check()
    return build(args.out, args.repo, args.ref)


if __name__ == '__main__':
    sys.exit(main())
