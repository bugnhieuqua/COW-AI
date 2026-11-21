SELECT " Id\, \Title\, \CreatedAt\, \Status\, (SELECT COUNT(*) FROM \Scenes\ WHERE \ProjectId\ = p.\Id\) as scenes FROM \Projects\ p ORDER BY \Id\ DESC;
