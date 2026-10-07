import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from check_update_notes import validate_update_notes

VALID='''V1.2.0-beta6 · 更新

遗物：
1. 第一项。
2. 第二项。

显示：
- 说明。

English

V1.2.0-beta6 · Update

Relics:
1. First.
2. Second.

Presentation:
- Note.
'''

class UpdateNoteStyle(unittest.TestCase):
    def test_valid_utf8_and_crlf(self):
        self.assertEqual(validate_update_notes(VALID.replace('\n','\r\n'),'1.2.0-beta6')['items_per_language'],3)
    def test_numbering_restarts_by_category(self):
        text=VALID.replace('- 说明。','1. 说明。').replace('- Note.','1. Note.')
        validate_update_notes(text,'1.2.0-beta6')
    def test_reject_incompatible_styles_and_incomplete_translation(self):
        invalid=[VALID.replace('English','[h2]English[/h2]'),
                 VALID.replace('English','English version'),
                 VALID.replace('V1.2.0-beta6 · Update','V1.2.0-beta5 · Update'),
                 VALID.replace('2. 第二项。','3. 第二项。'),
                 VALID.replace('2. Second.\n',''),
                 VALID.replace('\n\n显示：','\n显示：'),
                 VALID.replace('1. 第一项。','未编号的长段落。'),
                 VALID.replace('2. 第二项。','- 第二项。'),
                 VALID.replace('- 说明。','- '+('长'*370))]
        for text in invalid:
            with self.subTest(text=text):
                with self.assertRaises(ValueError):validate_update_notes(text,'1.2.0-beta6')

if __name__=='__main__':unittest.main()
