cutechess-cli ^
 -engine conf=dev name="Dev" ^
 -engine conf=base name="Base" ^
 -each tc=10+0.1 restart=on timemargin=10 ^
 -games 2500 -repeat 2 -resultformat wide -recover -wait 20 ^
 -maxmoves 200
 -ratinginterval 10 -variant standard -concurrency 8 ^
 -sprt elo0=0 elo1=10 alpha=0.05 beta=0.05 ^
 -event sprt-test -pgnout "./sprt.pgn" -site "UK" -tournament round-robin
 -openings file=".\engines\Opening\Cerebellum3Merge.bin" format=pgn ^
 -tb "./syzygy/3-4-5" -tbpieces 5 ^



