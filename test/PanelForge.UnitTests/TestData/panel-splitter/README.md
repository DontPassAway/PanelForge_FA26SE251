# Panel splitter parity data

`expected.json` = output of `split_page` in `src/PanelForge.PanelSplitter/app.py` on these PNGs.
Regenerate (from Backend/PanelForge_FA26SE251, needs Python + opencv-python):

    py -I test/PanelForge.UnitTests/TestData/panel-splitter/gen_expected.py src/PanelForge.PanelSplitter/app.py test/PanelForge.UnitTests/TestData/panel-splitter
