SET TERM ^ ;

CREATE OR ALTER PROCEDURE LOG_ITEM_CHANGE (
    ITEM_ID INTEGER,
    OLD_PRICE NUMERIC(10,2),
    NEW_PRICE NUMERIC(10,2)
)
AS
BEGIN
  -- tutaj normalnie byłby INSERT do AUDIT_LOG albo czegoś podobnego
  -- dla testu wystarczy pusta procedura
END^

SET TERM ; ^
