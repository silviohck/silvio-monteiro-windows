# Publicar o novo projeto

1. Abra https://github.com/new .
2. Nome do repositório: **silvio-monteiro-windows**. Marque **Private** até revisar segurança e licenças. Não gere README inicial (o pacote já contém um).
3. Extraia `silvio-monteiro-windows.zip` no Windows e abra o terminal na pasta extraída.
4. Execute:

```powershell
git init -b main
git add .
git commit -m "Inicia Orthck Windows com WPF, DOCX e fila de impressão"
git remote add origin https://github.com/silviohck/silvio-monteiro-windows.git
git push -u origin main
```

5. Abra **Actions → Windows — compilar e testar Orthck**. Quando concluído, a aba Artifacts conterá `Orthck-Windows-x64` se a compilação tiver sido aprovada.

**Não publique `dados.json`, documentos de pacientes nem modelos com informações identificáveis.**
