using AutoMapper;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Application.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<T_ROLE, RoleDto>().ForMember(d => d.Id, o => o.MapFrom(s => s.IDROLE)).ForMember(d => d.Nom, o => o.MapFrom(s => s.NOMROLE));
        CreateMap<T_DROIT, DroitDto>().ForMember(d => d.Id, o => o.MapFrom(s => s.IDDROIT)).ForMember(d => d.Nom, o => o.MapFrom(s => s.DROIT));
        CreateMap<T_BDD_SAGE, SageDbDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.ID))
            .ForMember(d => d.SqlAuth, o => o.MapFrom(s => s.TYPE_AUTH))
            .ForMember(d => d.NomBd, o => o.MapFrom(s => s.NOM_BD))
            .ForMember(d => d.Password, o => o.MapFrom(s => s.TMDP))
            .ForMember(d => d.Login, o => o.MapFrom(s => s.TLOGIN));
        CreateMap<SageDbDto, T_BDD_SAGE>()
            .ForMember(d => d.TYPE_AUTH, o => o.MapFrom(s => s.SqlAuth))
            .ForMember(d => d.NOM_BD, o => o.MapFrom(s => s.NomBd))
            .ForMember(d => d.TMDP, o => o.MapFrom(s => s.Password))
            .ForMember(d => d.TLOGIN, o => o.MapFrom(s => s.Login))
            .ForMember(d => d.SERVEUR, o => o.MapFrom(s => s.Serveur));
        CreateMap<T_BDD_POINTEUSE, PointeuseDbDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.ID))
            .ForMember(d => d.SqlAuth, o => o.MapFrom(s => s.TYPE_AUTH))
            .ForMember(d => d.NomBd, o => o.MapFrom(s => s.NOM_BD))
            .ForMember(d => d.Password, o => o.MapFrom(s => s.TMDP))
            .ForMember(d => d.Login, o => o.MapFrom(s => s.TLOGIN))
            .ForMember(d => d.TypePointage, o => o.MapFrom(s => s.TYPE_POINTAGE))
            .ForMember(d => d.Active, o => o.MapFrom(s => s.ACTIVE));
        CreateMap<PointeuseDbDto, T_BDD_POINTEUSE>()
            .ForMember(d => d.TYPE_AUTH, o => o.MapFrom(s => s.SqlAuth))
            .ForMember(d => d.NOM_BD, o => o.MapFrom(s => s.NomBd))
            .ForMember(d => d.TMDP, o => o.MapFrom(s => s.Password))
            .ForMember(d => d.TLOGIN, o => o.MapFrom(s => s.Login))
            .ForMember(d => d.TYPE_POINTAGE, o => o.MapFrom(s => s.TypePointage))
            .ForMember(d => d.ACTIVE, o => o.MapFrom(s => s.Active))
            .ForMember(d => d.SERVEUR, o => o.MapFrom(s => s.Serveur));
        CreateMap<T_CARDPAIE, CardPaieDto>()
            .ForMember(d => d.SageMatricule, o => o.MapFrom(s => s.SAGE_MATRICULE))
            .ForMember(d => d.SageNom, o => o.MapFrom(s => s.SAGE_NOM))
            .ForMember(d => d.SagePrenom, o => o.MapFrom(s => s.SAGE_PRENOM))
            .ForMember(d => d.PointeuseNumero, o => o.MapFrom(s => s.POINTEUSE_NUMERO))
            .ForMember(d => d.PointeuseNom, o => o.MapFrom(s => s.POINTEUSE_NOM))
            .ForMember(d => d.SageServeur, o => o.MapFrom(s => s.SAGE_SERVEUR))
            .ForMember(d => d.PointeuseServeur, o => o.MapFrom(s => s.POINTEUSE_SERVEUR))
            .ForMember(d => d.SageBdd, o => o.MapFrom(s => s.SAGE_BDD))
            .ForMember(d => d.PointeuseBdd, o => o.MapFrom(s => s.POINTEUSE_BDD));
        CreateMap<CardPaieDto, T_CARDPAIE>()
            .ForMember(d => d.SAGE_MATRICULE, o => o.MapFrom(s => s.SageMatricule))
            .ForMember(d => d.BRANCHE, o => o.MapFrom(s => s.Branche))
            .ForMember(d => d.SAGE_NOM, o => o.MapFrom(s => s.SageNom))
            .ForMember(d => d.SAGE_PRENOM, o => o.MapFrom(s => s.SagePrenom))
            .ForMember(d => d.POINTEUSE_NUMERO, o => o.MapFrom(s => s.PointeuseNumero))
            .ForMember(d => d.POINTEUSE_NOM, o => o.MapFrom(s => s.PointeuseNom))
            .ForMember(d => d.SAGE_SERVEUR, o => o.MapFrom(s => s.SageServeur))
            .ForMember(d => d.POINTEUSE_SERVEUR, o => o.MapFrom(s => s.PointeuseServeur))
            .ForMember(d => d.SAGE_BDD, o => o.MapFrom(s => s.SageBdd))
            .ForMember(d => d.POINTEUSE_BDD, o => o.MapFrom(s => s.PointeuseBdd));
        CreateMap<T_CATEGORIE, CategoryDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.IDCATEGORIE))
            .ForMember(d => d.SupervisorCardId, o => o.MapFrom(s => s.ID_CARDPAIE))
            .ForMember(d => d.HeuresSemaine, o => o.MapFrom(s => s.HEURESEMAINE));
        CreateMap<T_MAJORATION, MajorationDto>();
        CreateMap<T_CODE_CONSTANTE, CodeConstanteDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.ID))
            .ForMember(d => d.Categorie, o => o.MapFrom(s => s.CATEGORIE))
            .ForMember(d => d.Intitule, o => o.MapFrom(s => s.INTITULE))
            .ForMember(d => d.CodeConstante, o => o.MapFrom(s => s.CODE_CONSTANTE));
        CreateMap<T_FERIE, HolidayDto>();
        CreateMap<T_PLANNING, ShiftDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.IDPLANNING))
            .ForMember(d => d.NoShift, o => o.MapFrom(s => s.NoSHIFT))
            .ForMember(d => d.Ha, o => o.MapFrom(s => s.HA))
            .ForMember(d => d.Hd, o => o.MapFrom(s => s.HD));
    }
}
