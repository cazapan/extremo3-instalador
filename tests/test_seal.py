import importlib.util
import io
from pathlib import Path
import unittest
import zipfile

spec=importlib.util.spec_from_file_location('seal',Path(__file__).resolve().parents[1]/'tools'/'seal_release.py')
seal=importlib.util.module_from_spec(spec)
spec.loader.exec_module(seal)

class RequirementsTests(unittest.TestCase):
    def test_ranges(self):
        for expr,v in [('[21.1,)','21.1.265'),('[1.21.1,1.22)','1.21.1'),('[1.21.1]','1.21.1'),('[4,)','4.0.45')]:
            self.assertTrue(seal.contains(expr,v))
        self.assertFalse(seal.contains('[1.21.1,1.22)','1.22'))
        self.assertFalse(seal.contains('[21.1.266,)','21.1.265'))
        with self.assertRaises(ValueError): seal.contains('[1,2),[3,4)','1')

    def fixture(self,extra=''):
        txt='''modLoader="javafml"
loaderVersion="[4,)"
[[mods]]
modId="mcextremo"
version="14"
[[dependencies.mcextremo]]
modId="minecraft"
type="required"
versionRange="[1.21.1]"
[[dependencies.mcextremo]]
modId="neoforge"
type="required"
versionRange="[21.1,)"
'''+extra
        b=io.BytesIO()
        with zipfile.ZipFile(b,'w') as z: z.writestr('META-INF/neoforge.mods.toml',txt)
        return b.getvalue()

    def test_compatible(self):
        self.assertEqual(len(seal.inspect_mod(self.fixture(),'21.1.265')['dependencies']),2)

    def test_unresolved_dependency(self):
        with self.assertRaises(ValueError):
            seal.inspect_mod(self.fixture('''[[dependencies.mcextremo]]
modId="geckolib"
type="required"
versionRange="[4,)"
'''),'21.1.265')

    def test_wrong_version(self):
        with self.assertRaises(ValueError): seal.inspect_mod(self.fixture(),'21.0.1')

if __name__=='__main__': unittest.main()
