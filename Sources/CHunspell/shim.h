#ifdef __cplusplus
extern "C" {
#endif

typedef struct Hunhandle Hunhandle;

Hunhandle *Hunspell_create(const char *affpath, const char *dpath);
void Hunspell_destroy(Hunhandle *handle);
int Hunspell_spell(Hunhandle *handle, const char *word);
int Hunspell_suggest(Hunhandle *handle, char ***suggestion_list, const char *word);
void Hunspell_free_list(Hunhandle *handle, char ***suggestion_list, int count);

#ifdef __cplusplus
}
#endif
