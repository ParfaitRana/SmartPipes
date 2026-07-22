from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "assets" / "textures"


def build_item_sheet() -> None:
    sheet = Image.new("RGBA", (80, 16))
    outline = (55, 38, 38, 255)
    deepest = (83, 48, 39, 255)
    copper_dark = (126, 61, 36, 255)
    copper = (187, 91, 47, 255)
    copper_light = (231, 139, 72, 255)
    copper_shine = (255, 196, 111, 255)
    steel_dark = (62, 67, 72, 255)
    steel = (109, 119, 122, 255)
    steel_light = (181, 192, 188, 255)
    gold_dark = (151, 99, 30, 255)
    gold = (225, 167, 51, 255)
    gold_light = (255, 220, 102, 255)
    signal_dark = (20, 126, 139, 255)
    signal = (56, 220, 220, 255)

    # Pipe: an elbow segment fills the icon while staying readable as plumbing.
    pipe = Image.new("RGBA", (16, 16))
    draw = ImageDraw.Draw(pipe)
    draw.rectangle((1, 7, 11, 13), fill=outline)
    draw.rectangle((8, 2, 14, 10), fill=outline)
    draw.rectangle((3, 8, 10, 12), fill=copper_dark)
    draw.rectangle((9, 4, 13, 9), fill=copper_dark)
    draw.rectangle((4, 8, 9, 9), fill=copper_light)
    draw.rectangle((10, 4, 11, 8), fill=copper_light)
    draw.rectangle((9, 8, 12, 11), fill=copper)
    draw.point((10, 8), fill=copper_shine)
    draw.rectangle((0, 6, 3, 14), fill=outline)
    draw.rectangle((1, 7, 2, 13), fill=steel)
    draw.line((1, 8, 1, 11), fill=steel_light)
    draw.rectangle((7, 1, 15, 4), fill=outline)
    draw.rectangle((8, 2, 14, 3), fill=steel)
    draw.line((9, 2, 12, 2), fill=steel_light)
    draw.line((4, 11, 8, 11), fill=signal_dark)
    draw.line((5, 11, 7, 11), fill=signal)
    sheet.alpha_composite(pipe, (0, 0))

    # Port: beveled octagonal socket with one large, high-contrast flow arrow.
    port = Image.new("RGBA", (16, 16))
    draw = ImageDraw.Draw(port)
    draw.polygon(((4, 1), (11, 1), (14, 4), (14, 11), (11, 14), (4, 14), (1, 11), (1, 4)), fill=outline)
    draw.polygon(((4, 2), (11, 2), (13, 4), (13, 11), (11, 13), (4, 13), (2, 11), (2, 4)), fill=copper_dark)
    draw.line((4, 3, 10, 3), fill=copper_shine)
    draw.line((3, 4, 3, 10), fill=copper_light)
    draw.rectangle((4, 5, 11, 10), fill=deepest)
    draw.rectangle((5, 6, 10, 9), fill=steel_dark)
    draw.polygon(((6, 6), (11, 8), (6, 10)), fill=signal_dark)
    draw.polygon(((7, 7), (10, 8), (7, 9)), fill=signal)
    for point in ((4, 4), (11, 4), (4, 11), (11, 11)):
        draw.point(point, fill=gold_light)
    sheet.alpha_composite(port, (16, 0))

    # Shipping valve: a guarded square outlet with a dark discharge mouth.
    valve = Image.new("RGBA", (16, 16))
    draw = ImageDraw.Draw(valve)
    draw.polygon(((3, 1), (12, 1), (15, 4), (15, 11), (12, 14), (3, 14), (0, 11), (0, 4)), fill=outline)
    draw.polygon(((3, 2), (12, 2), (14, 4), (14, 10), (11, 13), (4, 13), (1, 10), (1, 4)), fill=gold_dark)
    draw.line((4, 3, 11, 3), fill=gold_light)
    draw.rectangle((3, 5, 12, 10), fill=deepest)
    draw.rectangle((4, 6, 11, 9), fill=steel_dark)
    draw.rectangle((5, 7, 10, 9), fill=(28, 35, 39, 255))
    draw.line((5, 6, 10, 6), fill=steel_light)
    draw.polygon(((5, 10), (10, 10), (9, 14), (6, 14)), fill=outline)
    draw.polygon(((6, 10), (9, 10), (8, 13), (7, 13)), fill=copper)
    draw.polygon(((6, 7), (10, 8), (6, 9)), fill=signal_dark)
    draw.polygon(((7, 7), (9, 8), (7, 9)), fill=signal)
    sheet.alpha_composite(valve, (32, 0))

    # Wrench: compact pipe-wrench silhouette based on the curved-jaw concept.
    # The gray jaws, copper adjustment body, and dark wrapped handle stay in
    # separate value groups so they remain readable at native 16x16 scale.
    wrench = Image.new("RGBA", (16, 16))
    draw = ImageDraw.Draw(wrench)
    wrench_pixels = (
        "                ",
        "   ....         ",
        "  .++-:.        ",
        " .+--::.        ",
        " .+-:.==.       ",
        " .::.=**.       ",
        "  ..=**=.       ",
        "   .==.::.      ",
        "   .+::--.      ",
        "    .+--:.      ",
        "     .:=%%.     ",
        "      .=%%.     ",
        "       .%%=.    ",
        "        .%%=.   ",
        "         .==.   ",
        "                ",
    )
    wrench_palette = {
        ".": outline,
        ":": steel_dark,
        "-": steel,
        "+": steel_light,
        "=": copper_dark,
        "*": copper,
        "#": copper_light,
        "%": deepest,
    }
    assert len(wrench_pixels) == 16 and all(len(row) == 16 for row in wrench_pixels)
    for y, row in enumerate(wrench_pixels):
        for x, pixel in enumerate(row):
            if pixel in wrench_palette:
                draw.point((x, y), fill=wrench_palette[pixel])
    sheet.alpha_composite(wrench, (48, 0))

    # Blueprint: a compact rolled plan with a cyan pipe diagram. It only
    # appears in Robin's shop, so the light paper silhouette is intentionally
    # distinct from the four dark copper-and-steel components.
    blueprint = Image.new("RGBA", (16, 16))
    draw = ImageDraw.Draw(blueprint)
    paper_dark = (79, 105, 119, 255)
    paper = (181, 219, 213, 255)
    paper_light = (232, 245, 224, 255)
    draw.polygon(((3, 2), (12, 2), (14, 4), (14, 13), (12, 15), (3, 15), (1, 13), (1, 4)), fill=outline)
    draw.rectangle((3, 3, 12, 13), fill=paper)
    draw.line((4, 4, 11, 4), fill=paper_light)
    draw.rectangle((1, 4, 4, 12), fill=paper_dark)
    draw.rectangle((2, 5, 4, 11), fill=paper_light)
    draw.rectangle((11, 4, 14, 12), fill=paper_dark)
    draw.rectangle((11, 5, 13, 11), fill=paper_light)
    draw.line((5, 7, 10, 7), fill=signal_dark)
    draw.line((7, 7, 7, 11), fill=signal_dark)
    draw.line((7, 10, 10, 10), fill=signal)
    draw.point((5, 7), fill=signal)
    draw.point((10, 7), fill=copper)
    draw.point((10, 10), fill=copper)
    sheet.alpha_composite(blueprint, (64, 0))

    sheet.save(OUTPUT / "items.png")
    sheet.resize((240, 48), Image.Resampling.NEAREST).save(OUTPUT / "items-preview-3x.png")


def build_pipe_sheet() -> None:
    dark = (40, 35, 38, 255)
    copper_shadow = (126, 57, 27, 255)
    copper = (205, 102, 48, 255)
    copper_light = (245, 157, 79, 255)
    signal = (64, 218, 218, 255)
    sheet = Image.new("RGBA", (64, 64))

    for mask in range(16):
        tile = Image.new("RGBA", (16, 16))
        draw = ImageDraw.Draw(tile)
        arms = {
            1: (6, 0, 10, 8),   # north
            2: (8, 6, 16, 10),  # east
            4: (6, 8, 10, 16),  # south
            8: (0, 6, 8, 10),   # west
        }
        for direction, rect in arms.items():
            if mask & direction:
                draw.rectangle(rect, fill=dark)
                inner = {
                    1: (7, 0, 9, 8),
                    2: (8, 7, 15, 9),
                    4: (7, 8, 9, 15),
                    8: (0, 7, 8, 9),
                }[direction]
                draw.rectangle(inner, fill=copper)

        draw.rectangle((5, 5, 10, 10), fill=dark)
        draw.rectangle((6, 6, 9, 9), fill=copper_shadow)
        draw.line((7, 7, 9, 7), fill=copper_light)
        draw.point((8, 8), fill=signal)

        x = (mask % 4) * 16
        y = (mask // 4) * 16
        sheet.alpha_composite(tile, (x, y))

    sheet.save(OUTPUT / "pipes.png")


def build_attachment_sheet() -> None:
    outline = (50, 38, 40, 255)
    copper_dark = (126, 61, 36, 255)
    copper = (190, 94, 48, 255)
    copper_light = (238, 151, 79, 255)
    steel_dark = (61, 68, 72, 255)
    gold_dark = (151, 99, 30, 255)
    gold = (225, 167, 51, 255)
    gold_light = (255, 220, 102, 255)
    signal_dark = (20, 126, 139, 255)
    signal = (65, 231, 226, 255)
    disabled = (213, 65, 54, 255)
    sheet = Image.new("RGBA", (64, 160))

    def arrow(draw: ImageDraw.ImageDraw, mode: int) -> None:
        if mode == 0:  # input: pipe edge -> object center
            draw.line((7, 1, 7, 10), fill=signal_dark, width=2)
            draw.polygon(((4, 8), (10, 8), (7, 12)), fill=signal)
        elif mode == 1:  # output: object center -> pipe edge
            draw.line((7, 3, 7, 12), fill=signal_dark, width=2)
            draw.polygon(((4, 4), (10, 4), (7, 0)), fill=signal)
        elif mode == 2:  # both
            draw.line((6, 2, 6, 11), fill=signal_dark)
            draw.polygon(((4, 4), (8, 4), (6, 1)), fill=signal)
            draw.line((9, 3, 9, 12), fill=signal_dark)
            draw.polygon(((7, 9), (11, 9), (9, 12)), fill=signal)
        else:
            draw.line((4, 5, 10, 11), fill=disabled, width=2)
            draw.line((10, 5, 4, 11), fill=disabled, width=2)

    for kind in range(2):
        for mode in range(5):
            north = Image.new("RGBA", (16, 16))
            draw = ImageDraw.Draw(north)
            if kind == 0:
                draw.rectangle((5, 0, 10, 5), fill=outline)
                draw.rectangle((6, 0, 9, 4), fill=steel_dark)
                draw.rectangle((4, 4, 11, 7), fill=outline)
                draw.rectangle((5, 5, 10, 6), fill=copper)
                draw.line((6, 5, 9, 5), fill=copper_light)
            else:
                draw.rectangle((5, 0, 10, 3), fill=outline)
                draw.rectangle((6, 0, 9, 2), fill=(27, 34, 38, 255))
                draw.polygon(((5, 2), (10, 2), (12, 7), (10, 9), (5, 9), (3, 7)), fill=outline)
                draw.polygon(((6, 3), (9, 3), (10, 7), (9, 8), (6, 8), (5, 7)), fill=gold_dark)
                draw.line((6, 4, 9, 4), fill=gold_light)
                draw.rectangle((4, 8, 11, 10), fill=outline)
                draw.rectangle((5, 8, 10, 9), fill=gold)

            if mode < 4:
                arrow(draw, mode)
            rotations = (
                north,
                north.transpose(Image.Transpose.ROTATE_270),
                north.transpose(Image.Transpose.ROTATE_180),
                north.transpose(Image.Transpose.ROTATE_90),
            )
            row = kind * 4 + mode if mode < 4 else 8 + kind
            for direction, tile in enumerate(rotations):
                sheet.alpha_composite(tile, (direction * 16, row * 16))

    sheet.save(OUTPUT / "attachments.png")
    sheet.resize((192, 480), Image.Resampling.NEAREST).save(OUTPUT / "attachments-preview-3x.png")


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    build_item_sheet()
    build_pipe_sheet()
    build_attachment_sheet()
