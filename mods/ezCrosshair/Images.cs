// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
namespace EzCrosshair
{
    // The crosshairs, drawn pixel by pixel by art/make.py, kept here so the mod is a single dll.
    internal static class Images
    {
        public static string Get(string name)
        {
            switch (name)
            {
                case "brackets": return "iVBORw0KGgoAAAANSUhEUgAAABwAAAAQCAYAAAAFzx/vAAAAWklEQVR4XmNggIL/UADjUwtgNZdWlmHzDBO1fEIsYAT5DqSYkZGRkVhN5ACYPXT34aiFWAElqZruQcpCToqjJEXT3YfD30IwoCTVEQOQzad7kGIt0akJ0EMPAKmdT9JX6F5zAAAAAElFTkSuQmCC";
                case "chunky plus": return "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAPUlEQVR4XmNkQID/SGxkwAjiMOGQxAAg1Sgm/f8P4TIygg2CA9JNhJmEDmAmk24iDFDFjXDDMBwIAaSFIwDRoA4NAe+iYAAAAABJRU5ErkJggg==";
                case "circle": return "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAAZUlEQVR4XrVSWw7AIAhbd/87s9AJoWgW/VgTH2gtDYhrhrUj1LgGJJopH0gKNxFZEAuBaOe4Jd2r5lOOLjBE6KN7r3AH5EiGHfz/IJAeV30J/34nlkYJowBcp74UxaPGSQm/vsYDpgIy+MPIPQkAAAAASUVORK5CYII=";
                case "cross": return "iVBORw0KGgoAAAANSUhEUgAAAAwAAAAMCAYAAABWdVznAAAALklEQVR4XmNgwAL+QwE2OSZsgvgA7TWQDBhB/kPWxcjIyIjMR5cn2Q/UAUMsHgAVnBf88yKcCwAAAABJRU5ErkJggg==";
                case "dark diamond": return "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAN0lEQVR4XmNgQAL/oQBZDA5AcgwMDGAMZWOXRFfECFLNyMiIy1QGJqwy6ADZfqLcgeFIGMDmTQDgk0LEjz+TYAAAAABJRU5ErkJggg==";
                case "diamond": return "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAQklEQVR4Xm2PAQoAIAgDXf//82LSRKuDQtyhFTFhRJ5itZokdalVkoUMTZcgu4cdAGPFF62AzJvTg98wJIe/kc83N+UWGAcylSAFAAAAAElFTkSuQmCC";
                case "dot": return "iVBORw0KGgoAAAANSUhEUgAAAAYAAAAGCAYAAADgzO9IAAAANklEQVR4XmWNSQ4AMAgCof//Mw2G4qGTiEpcgEXAxHCSJcnidoQuYhSS3fjwKXrikXqN3O7zCyM6EAObw5+4AAAAAElFTkSuQmCC";
                case "outlined plus": return "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAV0lEQVR4Xt2SQQoAIAgE3ej/X95QOpVrQbcGOimbjMI0XErIWlsRYCTjVZQBN3wQ0M0224rUpq+GJ9MKAO8SYwI587wB/0nhDlT1/RJv+OCQtN59O2nvAHo7ExhRrP+iAAAAAElFTkSuQmCC";
                case "pinhole": return "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAANklEQVR4XmNgQAX/GRjAGA4YoSwUQWR5JjQTMADIBLju//8hTEZGmMEMDESZAALkuwEdYHgTAK1nCgWlDP1mAAAAAElFTkSuQmCC";
                case "plus": return "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAJElEQVR4XmNgQAP/oQBdnAldABegvkJGkJOIsZ5oqzHAIPQ1AHB8E/zCtWeYAAAAAElFTkSuQmCC";
                case "square": return "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAFklEQVR4XmP8////fwYkwITMAQHCAgAGXQQEwXd60wAAAABJRU5ErkJggg==";
                case "star": return "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAQ0lEQVR4XmNgQAP/oQBdnAldABdAUQgyDKYQmQ0CcIXoEiCALMaITQE2QLQbGXG5CQYYGRnBauAmwgSQATYxOKA4HAEq/ifzKFQq1QAAAABJRU5ErkJggg==";
                default: return null;
            }
        }
    }
}
