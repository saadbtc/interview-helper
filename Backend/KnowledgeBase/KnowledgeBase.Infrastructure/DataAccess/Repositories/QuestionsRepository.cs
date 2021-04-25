using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowledgeBase.Application.Repositories;
using KnowledgeBase.Domain;
using KnowledgeBase.Infrastructure.DataAccess.InternalRepositories;
using KnowledgeBase.Infrastructure.DataAccess.Models;

namespace KnowledgeBase.Infrastructure.DataAccess.Repositories
{
    public class QuestionsRepository : IQuestionsRepository
    {
        private readonly QuestionTableRepresentationsRepository _questionTableRepresentationsRepository;
        private readonly QuestionLinkedTagsRepository _questionLinkedTagsRepository;
        private readonly LinkedTagsRepository _linkedTagsRepository;
        private readonly TagTableRepresentationsRepository _tagTableRepresentationsRepository;

        public QuestionsRepository(QuestionTableRepresentationsRepository questionTableRepresentationsRepository,
            QuestionLinkedTagsRepository questionLinkedTagsRepository, LinkedTagsRepository linkedTagsRepository,
            TagTableRepresentationsRepository tagTableRepresentationsRepository)
        {
            _questionTableRepresentationsRepository = questionTableRepresentationsRepository;
            _questionLinkedTagsRepository = questionLinkedTagsRepository;
            _linkedTagsRepository = linkedTagsRepository;
            _tagTableRepresentationsRepository = tagTableRepresentationsRepository;
        }

        public async Task<int> AddQuestion(Question target)
        {
            var questionTableRepresentation = new QuestionTableRepresentation
            {
                Title = target.Title,
                Answer = target.Answer,
                Id = target.Id
            };
            await _questionTableRepresentationsRepository.Add(questionTableRepresentation);

            foreach (var tag in target.TagsInformation)
            {
                var tagTableRepresentation = _tagTableRepresentationsRepository.GetByName(tag.Key);

                if (tagTableRepresentation == null)
                {
                    tagTableRepresentation = new TagTableRepresentation { Name = tag.Key };
                    await _tagTableRepresentationsRepository.Add(tagTableRepresentation);
                }

                foreach (var tagValue in tag.Value)
                {
                    var linkedTag = _linkedTagsRepository.GetByTagIdAndValue(tagTableRepresentation.Id, tagValue);
                    if (linkedTag == null)
                    {
                        linkedTag = new LinkedTag { TagId = tagTableRepresentation.Id, Value = tagValue };
                        await _linkedTagsRepository.Add(linkedTag);
                    }

                    await _questionLinkedTagsRepository.Add(new QuestionLinkedTag { LinkedTagId = linkedTag.Id, QuestionId = questionTableRepresentation.Id });
                }
            }

            return questionTableRepresentation.Id;
        }

        public Task<Question> GetQuestion(int id)
        {
            var tableRepresentation = _questionTableRepresentationsRepository.GetById(id);

            if (tableRepresentation == null)
            {
                return Task.FromResult(null as Question);
            }

            var linkedTags = _questionLinkedTagsRepository.GetByQuestionId(id).Select(_linkedTagsRepository.GetByQuestionLinkedTag);

            var tagsData = new Dictionary<string, List<string>>();
            foreach (var currentTag in linkedTags)
            {
                var tag = _tagTableRepresentationsRepository.GetByLinkedTag(currentTag);
                var tagName = tag.Name;
                if (!tagsData.ContainsKey(tagName))
                {
                    tagsData.Add(tagName, new List<string>());
                }

                tagsData[tagName].Add(currentTag.Value);
            }

            return Task.FromResult(new Question
            {
                Answer = tableRepresentation.Answer,
                TagsInformation = tagsData,
                Title = tableRepresentation.Title,
                Id = tableRepresentation.Id
            });
        }

        public Task<Question[]> GetQuestions(string tagName, string[] tagValues)
        {
            var tag = _tagTableRepresentationsRepository.GetByName(tagName);

            if (tag == null)
            {
                return Task.FromResult(new Question[] { });
            }

            var linkedTags = tagValues.Select(x => _linkedTagsRepository.GetByTagIdAndValue(tag.Id, x)).Where(x => x != null);
            var questionIds = linkedTags.SelectMany(x => _questionLinkedTagsRepository.GetByLinkedTag(x)).Select(x => x.QuestionId);
            var getQuestionTasks = questionIds.Select(x => GetQuestion(x));

            return Task.FromResult(getQuestionTasks.Select(x => x.Result).Where(x => x != null).ToArray());
        }

        public Task RemoveQuestion(int id)
        {
            throw new System.NotImplementedException();
        }

        public async Task<(int createdTagValues, int existedTagValues)> LinkTags(int questionId, string tagTitle, string[] tagValues)
        {
            var createdTagValues = 0;
            var existedTagValues = 0;

            var tag = _tagTableRepresentationsRepository.GetByName(tagTitle);

            if (tag == null)
            {
                tag = new TagTableRepresentation { Name = tagTitle };
                await _tagTableRepresentationsRepository.Add(tag);
            }

            foreach (var tagValue in tagValues)
            {
                var newTagValue = false;
                var linkedTag = _linkedTagsRepository.GetByTagIdAndValue(tag.Id, tagValue);

                if (linkedTag == null)
                {
                    newTagValue = true;
                    linkedTag = new LinkedTag { TagId = tag.Id, Value = tagValue };
                    await _linkedTagsRepository.Add(linkedTag);
                }

                var associatedQuestionLinkedTags = _questionLinkedTagsRepository.GetByLinkedTag(linkedTag);
                if (!associatedQuestionLinkedTags.Any(x => x.QuestionId == questionId))
                {
                    await _questionLinkedTagsRepository.Add(new QuestionLinkedTag { LinkedTagId = linkedTag.Id, QuestionId = questionId });
                }

                if (!newTagValue)
                {
                    existedTagValues++;
                    continue;
                }

                createdTagValues++;
            }

            return (createdTagValues, existedTagValues);
        }

        public async Task<int> WithdrawTags(int questionId, string tagTitle, string[] tagValues)
        {
            var tag = _tagTableRepresentationsRepository.GetByName(tagTitle);

            if (tag == null)
            {
                return 0;
            }

            var withdrawnTagsCount = 0;
            var associatedLinkedTags = tagValues.Select(x => _linkedTagsRepository.GetByTagIdAndValue(tag.Id, x));

            foreach (var linkedTag in associatedLinkedTags)
            {
                withdrawnTagsCount += await _questionLinkedTagsRepository.RemoveByQuestionIdAndLinkedTagId(
                    questionId,
                    linkedTag.Id
                );
            }

            return withdrawnTagsCount;
        }
    }
}